using Microsoft.EntityFrameworkCore;

namespace Monitoring.Blazor.Services;

public sealed class DataRetentionWorker(
    IDbContextFactory<MonitoringDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<DataRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Max(5, configuration.GetValue("Monitoring:Retention:CleanupIntervalMinutes", 60));
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Retention cleanup failed.");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var snapshotDays = Math.Max(1, configuration.GetValue("Monitoring:Retention:SnapshotDays", 30));
        var alertDays = Math.Max(1, configuration.GetValue("Monitoring:Retention:AlertDays", 90));
        var logStatsDays = Math.Max(1, configuration.GetValue("Monitoring:Retention:LogIpDailyStatDays", 365));

        var snapshotCutoff = DateTime.UtcNow.AddDays(-snapshotDays);
        var alertCutoff = DateTime.UtcNow.AddDays(-alertDays);
        var logStatsCutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-logStatsDays));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var snapshotsDeleted = await db.HostSnapshots
            .Where(x => x.CreatedUtc < snapshotCutoff)
            .ExecuteDeleteAsync(ct);
        var alertsDeleted = await db.AlertEvents
            .Where(x => x.TimestampUtc < alertCutoff)
            .ExecuteDeleteAsync(ct);
        var logStatsDeleted = await db.LogIpDailyStats
            .Where(x => x.LogDate < logStatsCutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Retention cleanup done. Snapshots {Snapshots}, Alerts {Alerts}, LogStats {LogStats}.",
            snapshotsDeleted,
            alertsDeleted,
            logStatsDeleted);
    }
}
