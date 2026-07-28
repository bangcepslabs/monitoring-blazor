namespace Monitoring.Blazor.Services;

public sealed class SettingsBackupWorker(
    SettingsBackupArchiveService archiveService,
    ILogger<SettingsBackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CreateBackupIfNeededAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CreateBackupIfNeededAsync(stoppingToken);
        }
    }

    private async Task CreateBackupIfNeededAsync(CancellationToken ct)
    {
        try
        {
            var latest = archiveService.GetLatestArchive();
            if (latest is not null && latest.CreatedUtc >= DateTime.UtcNow.AddHours(-20))
            {
                archiveService.CleanupOldArchives();
                return;
            }

            var created = await archiveService.CreateArchiveAsync(ct);
            if (created is not null)
            {
                logger.LogInformation("Created automatic settings backup {FileName}", created.FileName);
            }

            var removed = archiveService.CleanupOldArchives();
            if (removed > 0)
            {
                logger.LogInformation("Removed {Count} old backup archive(s).", removed);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create automatic settings backup.");
        }
    }
}
