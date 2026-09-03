using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class LogAutoImportService(
    IConfiguration configuration,
    RuntimeSettingsRepository runtimeSettingsRepository,
    IDbContextFactory<MonitoringDbContext> dbFactory,
    ILogger<LogAutoImportService> logger)
{
    public async Task<string> RunOnceAsync(CancellationToken ct)
    {
        var options = LoadOptions();
        if (!options.Enabled)
        {
            return "Log import is disabled.";
        }

        if (string.IsNullOrWhiteSpace(options.LogFolder))
        {
            return "Log import skipped. LogFolder not configured.";
        }

        if (string.IsNullOrWhiteSpace(options.StateFilePath))
        {
            return "Log import skipped. StateFilePath not configured.";
        }

        var stateDirectory = Path.GetDirectoryName(options.StateFilePath);
        if (!string.IsNullOrWhiteSpace(stateDirectory))
        {
            Directory.CreateDirectory(stateDirectory);
        }

        var targetDate = DateTime.Today.AddDays(-options.TargetDateOffsetDays);
        var files = new DirectoryInfo(options.LogFolder)
            .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
            .Where(file => IisLogFileNameParser.TryGetLogDate(file.Name, out var logDate) && logDate == DateOnly.FromDateTime(targetDate))
            .OrderBy(file => file.Name)
            .ToList();

        if (files.Count == 0)
        {
            return $"No IIS log files matched filename date for {targetDate:yyyy-MM-dd}.";
        }

        var state = LoadState(options.StateFilePath);
        var serverName = string.IsNullOrWhiteSpace(options.ServerName)
            ? Environment.MachineName
            : options.ServerName.Trim();

        var nowUtc = DateTime.UtcNow;
        var importedFiles = 0;
        var aggregateRows = 0L;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var fingerprint = $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
            if (state.ProcessedFiles.TryGetValue(file.FullName, out var known)
                && string.Equals(known.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                continue;
            }

            var result = await ImportFileAsync(db, file.FullName, serverName, nowUtc, ct);
            aggregateRows += result.AggregateRows;
            importedFiles++;

            state.ProcessedFiles[file.FullName] = new ProcessedFileState
            {
                Fingerprint = fingerprint,
                ProcessedAtUtc = nowUtc
            };
            SaveState(options.StateFilePath, state);
            logger.LogInformation("Imported IIS log file {Path}", file.FullName);
        }

        return importedFiles == 0
            ? $"No new IIS log files to import for {targetDate:yyyy-MM-dd}."
            : $"Imported {importedFiles} file(s), {aggregateRows:N0} aggregate row(s).";
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

    private async Task<ImportResult> ImportFileAsync(
        MonitoringDbContext db,
        string fullPath,
        string serverName,
        DateTime nowUtc,
        CancellationToken ct)
    {
        const int batchSize = 2000;
        var bufferLines = new List<string>(batchSize);
        var aggregateMap = new Dictionary<(DateOnly LogDate, string Ip), LogIpAggregate>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            bufferLines.Add(line);
            if (bufferLines.Count < batchSize)
            {
                continue;
            }

            await FlushBatchAsync(bufferLines, nowUtc, aggregateMap, ct);
            bufferLines.Clear();
        }

        if (bufferLines.Count > 0)
        {
            await FlushBatchAsync(bufferLines, nowUtc, aggregateMap, ct);
            bufferLines.Clear();
        }

        foreach (var aggregate in aggregateMap.Values)
        {
            var existing = await db.LogIpDailyStats.FirstOrDefaultAsync(x =>
                x.ServerName == serverName &&
                x.LogDate == aggregate.LogDate &&
                x.Ip == aggregate.Ip,
                ct);

            if (existing is null)
            {
                db.LogIpDailyStats.Add(new LogIpDailyStatEntity
                {
                    ServerName = serverName,
                    LogDate = aggregate.LogDate,
                    Ip = aggregate.Ip,
                    RequestCount = aggregate.RequestCount,
                    Status2xxCount = aggregate.Status2xxCount,
                    Status3xxCount = aggregate.Status3xxCount,
                    Status4xxCount = aggregate.Status4xxCount,
                    Status5xxCount = aggregate.Status5xxCount,
                    FirstSeenUtc = nowUtc,
                    LastSeenUtc = nowUtc
                });
            }
            else
            {
                existing.RequestCount += aggregate.RequestCount;
                existing.Status2xxCount += aggregate.Status2xxCount;
                existing.Status3xxCount += aggregate.Status3xxCount;
                existing.Status4xxCount += aggregate.Status4xxCount;
                existing.Status5xxCount += aggregate.Status5xxCount;
                existing.LastSeenUtc = nowUtc;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();

        return new ImportResult(aggregateMap.Count);
    }

    private Task FlushBatchAsync(
        List<string> bufferLines,
        DateTime nowUtc,
        Dictionary<(DateOnly LogDate, string Ip), LogIpAggregate> aggregateMap,
        CancellationToken ct)
    {
        var parsed = ApacheLogParser.ParseLines(bufferLines);
        if (parsed.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var aggregate in parsed
            .Select(row => new
            {
                LogDate = TryParseLogDate(row.Date, nowUtc),
                row.Ip,
                row.Status
            })
            .GroupBy(x => new { x.LogDate, x.Ip })
            .Select(g => new LogIpAggregate
            {
                LogDate = g.Key.LogDate,
                Ip = g.Key.Ip,
                RequestCount = g.LongCount(),
                Status2xxCount = g.LongCount(x => IsStatusInRange(x.Status, 200, 299)),
                Status3xxCount = g.LongCount(x => IsStatusInRange(x.Status, 300, 399)),
                Status4xxCount = g.LongCount(x => IsStatusInRange(x.Status, 400, 499)),
                Status5xxCount = g.LongCount(x => IsStatusInRange(x.Status, 500, 599))
            }))
        {
            var key = (aggregate.LogDate, aggregate.Ip);
            if (aggregateMap.TryGetValue(key, out var existing))
            {
                existing.RequestCount += aggregate.RequestCount;
                existing.Status2xxCount += aggregate.Status2xxCount;
                existing.Status3xxCount += aggregate.Status3xxCount;
                existing.Status4xxCount += aggregate.Status4xxCount;
                existing.Status5xxCount += aggregate.Status5xxCount;
            }
            else
            {
                aggregateMap[key] = aggregate;
            }
        }

        return Task.CompletedTask;
    }

    private LogImportOptions LoadOptions()
    {
        var section = LoadLogImportSettings();
        return new LogImportOptions
        {
            Enabled = section.Enabled,
            LogFolder = section.LogFolder,
            RunHour = section.RunHour,
            RunMinute = section.RunMinute,
            TargetDateOffsetDays = section.TargetDateOffsetDays,
            StateFilePath = section.StateFilePath,
            ServerName = section.ServerName
        };
    }

    private LogImportRuntimeSettings LoadLogImportSettings()
    {
        var fallback = configuration.GetSection("Monitoring:LogImport").Get<LogImportRuntimeSettings>() ?? new LogImportRuntimeSettings();
        return runtimeSettingsRepository.LoadLogImport(fallback);
    }

    private static ImportState LoadState(string path)
    {
        if (!File.Exists(path))
        {
            return new ImportState();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ImportState>(json) ?? new ImportState();
    }

    private static void SaveState(string path, ImportState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static DateOnly TryParseLogDate(string value, DateTime fallbackUtc)
    {
        return DateOnly.TryParse(value, out var parsed) ? parsed : DateOnly.FromDateTime(fallbackUtc);
    }

    private static bool IsStatusInRange(string value, int minInclusive, int maxInclusive)
    {
        if (!int.TryParse(value, out var status))
        {
            return false;
        }

        return status >= minInclusive && status <= maxInclusive;
    }

    private sealed class LogImportOptions
    {
        public bool Enabled { get; init; }
        public string LogFolder { get; init; } = string.Empty;
        public int RunHour { get; init; }
        public int RunMinute { get; init; }
        public int TargetDateOffsetDays { get; init; }
        public string StateFilePath { get; init; } = string.Empty;
        public string ServerName { get; init; } = string.Empty;
    }

    private sealed class ImportState
    {
        public Dictionary<string, ProcessedFileState> ProcessedFiles { get; set; } = [];
    }

    private sealed class ProcessedFileState
    {
        public string Fingerprint { get; set; } = string.Empty;
        public DateTime ProcessedAtUtc { get; set; }
    }

    private sealed record ImportResult(int AggregateRows);
}
