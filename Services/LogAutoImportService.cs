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
        string[]? fieldOrder = null;

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

            FlushBatch(bufferLines, nowUtc, aggregateMap, ref fieldOrder);
            bufferLines.Clear();
        }

        if (bufferLines.Count > 0)
        {
            FlushBatch(bufferLines, nowUtc, aggregateMap, ref fieldOrder);
            bufferLines.Clear();
        }

        await LogIpDailyStatsPersistenceService.AddAggregatesAsync(
            db,
            serverName,
            aggregateMap.Values,
            nowUtc,
            cancellationToken: ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();

        return new ImportResult(aggregateMap.Count);
    }

    private static void FlushBatch(
        List<string> bufferLines,
        DateTime nowUtc,
        Dictionary<(DateOnly LogDate, string Ip), LogIpAggregate> aggregateMap,
        ref string[]? fieldOrder)
    {
        var parsed = ApacheLogParser.ParseLines(bufferLines, ref fieldOrder);
        IisLogAggregationService.AddRows(aggregateMap, parsed, nowUtc);
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
