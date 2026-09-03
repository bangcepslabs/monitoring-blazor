using Microsoft.EntityFrameworkCore;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class ApiMetricsPersistenceWorker(
    ApiMetricsState metrics,
    IDbContextFactory<MonitoringDbContext> dbFactory,
    MonitoringHealthState healthState,
    ILogger<ApiMetricsPersistenceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PersistAsync(stoppingToken);
                healthState.MarkSuccess("api-metrics-persistence");
            }
            catch (Exception ex)
            {
                healthState.MarkFailure("api-metrics-persistence", ex);
                logger.LogWarning(ex, "Failed to persist API metrics.");
            }
        }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var bucket = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var metric in metrics.TakeSnapshotAndReset())
        {
            var row = await db.ApiMetricBuckets.SingleOrDefaultAsync(x => x.BucketStartUtc == bucket && x.Path == metric.Path, ct);
            if (row is null)
                db.ApiMetricBuckets.Add(new ApiMetricBucketEntity { BucketStartUtc = bucket, Path = metric.Path, Requests = metric.Requests, Errors = metric.Errors, TotalMs = (long)Math.Round(metric.AverageMs * metric.Requests) });
            else
            {
                row.Requests = metric.Requests;
                row.Errors = metric.Errors;
                row.TotalMs = (long)Math.Round(metric.AverageMs * metric.Requests);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
