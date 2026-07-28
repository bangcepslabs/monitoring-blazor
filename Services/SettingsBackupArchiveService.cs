using System.Text.Json;

namespace Monitoring.Blazor.Services;

public sealed class SettingsBackupArchiveService(
    SettingsBackupService backupService,
    IConfiguration configuration,
    ILogger<SettingsBackupArchiveService> logger)
{
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public async Task<SettingsBackupArchiveEntry?> CreateArchiveAsync(CancellationToken ct = default)
    {
        try
        {
            var directory = GetArchiveDirectory();
            Directory.CreateDirectory(directory);

            var payload = backupService.CreateBackup();
            var createdUtc = DateTime.UtcNow;
            var fileName = $"settings-backup-{createdUtc:yyyyMMdd-HHmmss}.json";
            var path = Path.Combine(directory, fileName);
            var json = JsonSerializer.Serialize(payload, _options);
            await File.WriteAllTextAsync(path, json, ct);
            var removed = CleanupOldArchives();
            if (removed > 0)
            {
                logger.LogInformation("Removed {Count} old backup archive(s).", removed);
            }

            return new SettingsBackupArchiveEntry(
                fileName,
                path,
                createdUtc,
                new FileInfo(path).Length,
                "settings");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create settings backup archive.");
            return null;
        }
    }

    public List<SettingsBackupArchiveEntry> GetRecentArchives(int take = 10)
    {
        take = take <= 0 ? 10 : Math.Min(take, 50);
        var directory = GetArchiveDirectory();
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, "settings-backup-*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Take(take)
            .Select(file => new SettingsBackupArchiveEntry(
                file.Name,
                file.FullName,
                file.CreationTimeUtc,
                file.Length,
                "settings"))
            .ToList();
    }

    public SettingsBackupArchiveEntry? GetLatestArchive()
    {
        return GetRecentArchives(1).FirstOrDefault();
    }

    public SettingsBackupPolicy GetPolicy()
    {
        return new SettingsBackupPolicy(
            GetKeepCount(),
            GetRetentionDays(),
            GetArchiveDirectory());
    }

    public int CleanupOldArchives()
    {
        var directory = GetArchiveDirectory();
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var policy = GetPolicy();
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, policy.RetentionDays));
        var files = Directory.GetFiles(directory, "settings-backup-*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .ToList();

        var toKeep = files.Take(Math.Max(1, policy.KeepCount)).ToHashSet();
        var removed = 0;

        foreach (var file in files)
        {
            if (toKeep.Contains(file) || file.CreationTimeUtc >= cutoff)
            {
                continue;
            }

            try
            {
                file.Delete();
                removed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete backup archive {File}", file.FullName);
            }
        }

        return removed;
    }

    private string GetArchiveDirectory()
    {
        var configured = configuration.GetValue<string>("Monitoring:BackupDirectory");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var dataDir = configuration.GetValue<string>("Monitoring:DataDirectory");
        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            return Path.Combine(dataDir, "backups");
        }

        return Path.Combine(AppContext.BaseDirectory, "backups");
    }

    private int GetKeepCount() => Math.Clamp(configuration.GetValue("Monitoring:Backup:KeepCount", 30), 1, 365);

    private int GetRetentionDays() => Math.Clamp(configuration.GetValue("Monitoring:Backup:RetentionDays", 30), 1, 3650);
}

public sealed record SettingsBackupArchiveEntry(
    string FileName,
    string FullPath,
    DateTime CreatedUtc,
    long SizeBytes,
    string Kind);

public sealed record SettingsBackupPolicy(
    int KeepCount,
    int RetentionDays,
    string Directory);
