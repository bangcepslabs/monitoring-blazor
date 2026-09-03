using System.Text;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class LogAutoExportService(
    IConfiguration configuration,
    RuntimeSettingsRepository runtimeSettingsRepository,
    ILogger<LogAutoExportService> logger,
    OllamaClient ollamaClient,
    OllamaAnalysisStore analysisStore)
{
    private const string StateFileName = "log-export-state.json";

    public async Task<string> RunOnceAsync(CancellationToken ct)
    {
        var options = LoadOptions();
        if (!options.Enabled)
        {
            return "Log export is disabled.";
        }

        if (string.IsNullOrWhiteSpace(options.LogFolder) || string.IsNullOrWhiteSpace(options.OutputFolder))
        {
            return "Log export skipped. LogFolder/OutputFolder not configured.";
        }

        Directory.CreateDirectory(options.OutputFolder);

        var targetDate = DateTime.Today.AddDays(-options.TargetDateOffsetDays);
        var state = LoadState(options.OutputFolder);
        if (state.LastRunDate == DateOnly.FromDateTime(targetDate))
        {
            return $"Already exported for {targetDate:yyyy-MM-dd}.";
        }

        var files = new DirectoryInfo(options.LogFolder)
            .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
            .Where(file => IisLogFileNameParser.TryGetLogDate(file.Name, out var logDate) && logDate == DateOnly.FromDateTime(targetDate))
            .OrderBy(file => file.Name)
            .ToList();

        if (files.Count == 0)
        {
            state.LastRunDate = DateOnly.FromDateTime(targetDate);
            SaveState(options.OutputFolder, state);
            return $"No IIS log files matched filename date for {targetDate:yyyy-MM-dd}.";
        }

        var stats = new LogExportStats();
        var outputName = $"iis-log-{targetDate:yyyyMMdd}.csv";
        var outputPath = Path.Combine(options.OutputFolder, outputName);

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync("date,time,ip,method,uri,status,referrer,user_agent");

        foreach (var file in files)
        {
            await ExportFileAsync(file.FullName, writer, stats, ct);
        }

        await writer.FlushAsync();
        state.LastRunDate = DateOnly.FromDateTime(targetDate);
        SaveState(options.OutputFolder, state);
        logger.LogInformation("IIS log export completed: {Path}", outputPath);

        await TryAutoAnalyzeAsync(targetDate, stats, ct);

        var ratio = stats.Total == 0 ? 0 : stats.Status5xx / (double)stats.Total;
        return $"Exported {files.Count} file(s) to {outputPath} (5xx ratio {ratio:P2})";
    }

    public TimeSpan GetDelayToNextRun()
    {
        var now = DateTime.Now;
        var options = LoadOptions();
        var next = new DateTime(now.Year, now.Month, now.Day, options.RunHour, options.RunMinute, 0);
        if (now >= next)
        {
            next = next.AddDays(1);
        }
        return next - now;
    }

    private async Task ExportFileAsync(string path, StreamWriter writer, LogExportStats stats, CancellationToken ct)
    {
        const int batchSize = 2000;
        var buffer = new List<string>(batchSize);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            buffer.Add(line);
            if (buffer.Count < batchSize)
            {
                continue;
            }

            var rows = ApacheLogParser.ParseLines(buffer);
            stats.AddRows(rows);
            WriteRows(rows, writer);
            buffer.Clear();
        }

        if (buffer.Count > 0)
        {
            var rows = ApacheLogParser.ParseLines(buffer);
            stats.AddRows(rows);
            WriteRows(rows, writer);
            buffer.Clear();
        }
    }

    private static void WriteRows(IEnumerable<ParsedLogRow> rows, StreamWriter writer)
    {
        foreach (var row in rows)
        {
            var line = string.Join(",", new[]
            {
                Csv(row.Date),
                Csv(row.Time),
                Csv(row.Ip),
                Csv(row.Method),
                Csv(row.Uri),
                Csv(row.Status),
                Csv(row.Referrer),
                Csv(row.UserAgent)
            });
            writer.WriteLine(line);
        }
    }

    private static string Csv(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Contains('"') || v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
        {
            v = v.Replace("\"", "\"\"");
            return $"\"{v}\"";
        }
        return v;
    }

    private async Task TryAutoAnalyzeAsync(DateTime targetDate, LogExportStats stats, CancellationToken ct)
    {
        var settings = LoadOllamaSettings();
        var enabled = settings.Enabled;
        if (!enabled)
        {
            return;
        }

        var autoEnabled = settings.AutoAnalyzeEnabled;
        if (!autoEnabled || stats.Total == 0)
        {
            return;
        }

        var threshold = settings.AutoAnalyzeStatus5xxRatio;
        var ratio = stats.Status5xx / (double)stats.Total;
        if (ratio < threshold)
        {
            return;
        }

        try
        {
            var prompt = BuildAnalysisPrompt(targetDate, stats, ratio, threshold);
            var response = await ollamaClient.GenerateAsync(
                prompt,
                "당신은 IIS 로그를 분석하는 SRE 어시스턴트입니다. 반드시 한국어로만 답변하세요.",
                ct);

            var entry = new OllamaAnalysisEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Source = "auto",
                LogDate = DateOnly.FromDateTime(targetDate),
                TotalRows = stats.Total,
                Status5xxCount = stats.Status5xx,
                Status5xxRatio = ratio,
                TopStatusCodes = stats.TopStatusCodes(),
                Top5xxUris = stats.Top5xxUris(),
                Prompt = prompt,
                Response = response
            };

            await analysisStore.SaveAsync(entry, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto analysis failed.");
        }
    }

    private static string BuildAnalysisPrompt(DateTime targetDate, LogExportStats stats, double ratio, double threshold)
    {
        var statusLines = stats.TopStatusCodes()
            .Select(x => $"- {x.Key}: {x.Count:N0}")
            .ToList();
        var uriLines = stats.Top5xxUris()
            .Select(x => $"- {x.Key}: {x.Count:N0}")
            .ToList();

        return $"""
            IIS 로그 자동 분석 요청
            날짜: {targetDate:yyyy-MM-dd}
            총 요청 수: {stats.Total:N0}
            5xx 수: {stats.Status5xx:N0}
            5xx 비율: {ratio:P2} (임계치 {threshold:P2})

            상태 코드 TOP:
            {string.Join(Environment.NewLine, statusLines)}

            5xx URI TOP:
            {(uriLines.Count == 0 ? "- 없음" : string.Join(Environment.NewLine, uriLines))}

            분석 요구사항:
            1) 5xx 증가 원인 가설
            2) 우선 대응 항목
            3) 재발 방지 체크리스트
            위 항목을 한국어로 간단명료하게 정리해줘.
            """;
    }

    private LogExportOptions LoadOptions()
    {
        var section = LoadLogExportSettings();
        return new LogExportOptions
        {
            Enabled = section.Enabled,
            LogFolder = section.LogFolder,
            OutputFolder = section.OutputFolder,
            RunHour = section.RunHour,
            RunMinute = section.RunMinute,
            TargetDateOffsetDays = section.TargetDateOffsetDays
        };
    }

    private LogExportRuntimeSettings LoadLogExportSettings()
    {
        var fallback = configuration.GetSection("Monitoring:LogExport").Get<LogExportRuntimeSettings>() ?? new LogExportRuntimeSettings();
        return runtimeSettingsRepository.LoadLogExport(fallback);
    }

    private OllamaRuntimeSettings LoadOllamaSettings()
    {
        var fallback = configuration.GetSection("Monitoring:Ollama").Get<OllamaRuntimeSettings>() ?? new OllamaRuntimeSettings();
        return runtimeSettingsRepository.LoadOllama(fallback);
    }

    private static LogExportState LoadState(string outputFolder)
    {
        var path = Path.Combine(outputFolder, StateFileName);
        if (!File.Exists(path))
        {
            return new LogExportState();
        }

        var json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<LogExportState>(json) ?? new LogExportState();
    }

    private static void SaveState(string outputFolder, LogExportState state)
    {
        var path = Path.Combine(outputFolder, StateFileName);
        var json = System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private sealed class LogExportOptions
    {
        public bool Enabled { get; init; }
        public string LogFolder { get; init; } = string.Empty;
        public string OutputFolder { get; init; } = string.Empty;
        public int RunHour { get; init; }
        public int RunMinute { get; init; }
        public int TargetDateOffsetDays { get; init; }
    }

    private sealed class LogExportState
    {
        public DateOnly? LastRunDate { get; set; }
    }

    private sealed class LogExportStats
    {
        private const int MaxUriKeys = 5000;
        public long Total { get; private set; }
        public long Status5xx { get; private set; }
        private readonly Dictionary<string, long> _statusCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _uri5xxCounts = new(StringComparer.OrdinalIgnoreCase);

        public void AddRows(IEnumerable<ParsedLogRow> rows)
        {
            foreach (var row in rows)
            {
                Total++;
                if (!string.IsNullOrWhiteSpace(row.Status))
                {
                    _statusCounts[row.Status] = _statusCounts.TryGetValue(row.Status, out var count) ? count + 1 : 1;
                }

                if (row.Status is not null && row.Status.StartsWith("5", StringComparison.Ordinal))
                {
                    Status5xx++;
                    if (!string.IsNullOrWhiteSpace(row.Uri))
                    {
                        if (_uri5xxCounts.Count < MaxUriKeys || _uri5xxCounts.ContainsKey(row.Uri))
                        {
                            _uri5xxCounts[row.Uri] = _uri5xxCounts.TryGetValue(row.Uri, out var uriCount) ? uriCount + 1 : 1;
                        }
                    }
                }
            }
        }

        public List<CountItem> TopStatusCodes(int take = 5)
        {
            return _statusCounts
                .OrderByDescending(x => x.Value)
                .Take(take)
                .Select(x => new CountItem(x.Key, x.Value))
                .ToList();
        }

        public List<CountItem> Top5xxUris(int take = 5)
        {
            return _uri5xxCounts
                .OrderByDescending(x => x.Value)
                .Take(take)
                .Select(x => new CountItem(x.Key, x.Value))
                .ToList();
        }
    }
}

