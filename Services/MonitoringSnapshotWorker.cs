using Microsoft.EntityFrameworkCore;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class MonitoringSnapshotWorker(
    MonitoringSnapshotQueue queue,
    IDbContextFactory<MonitoringDbContext> dbFactory,
    ILogger<MonitoringSnapshotWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var info in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                db.HostSnapshots.Add(ToEntity(info));
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to persist monitoring snapshot.");
            }
        }
    }

    private static HostSnapshotEntity ToEntity(MonitoringInfo info)
    {
        return new HostSnapshotEntity
        {
            Hostname = info.Hostname,
            Ip = info.Ip,
            Os = info.Os,
            TargetUrl = info.TargetUrl,
            Status = info.Status,
            CpuUsage = info.Dynamic.CpuInfo.Usage,
            MemoryUsage = info.Dynamic.MemoryInfo.Usage,
            DiskUsage = info.Dynamic.DiskInfo.Percent,
            SentMbps = info.Dynamic.NetworkInfo.SentMbps,
            RecvMbps = info.Dynamic.NetworkInfo.RecvMbps,
            BytesSent = unchecked((long)info.Dynamic.NetworkInfo.BytesSent),
            BytesRecv = unchecked((long)info.Dynamic.NetworkInfo.BytesRecv),
            CreatedUtc = DateTime.UtcNow
        };
    }
}
