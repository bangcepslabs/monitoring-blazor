using Microsoft.EntityFrameworkCore;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public static class LogIpDailyStatsPersistenceService
{
    public static async Task<int> AddAggregatesAsync(
        MonitoringDbContext db,
        string serverName,
        IEnumerable<LogIpAggregate> aggregates,
        DateTime nowUtc,
        Func<int, int, Task>? reportProgressAsync = null,
        CancellationToken cancellationToken = default)
    {
        var rows = aggregates.ToList();
        if (rows.Count == 0)
        {
            return 0;
        }

        var firstDate = rows.Min(row => row.LogDate);
        var lastDate = rows.Max(row => row.LogDate);
        var existingRows = await db.LogIpDailyStats
            .Where(row => row.ServerName == serverName && row.LogDate >= firstDate && row.LogDate <= lastDate)
            .ToListAsync(cancellationToken);
        var existingByKey = existingRows.ToDictionary(row => (row.LogDate, row.Ip));

        for (var index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var aggregate = rows[index];
            if (existingByKey.TryGetValue((aggregate.LogDate, aggregate.Ip), out var existing))
            {
                existing.RequestCount += aggregate.RequestCount;
                existing.Status2xxCount += aggregate.Status2xxCount;
                existing.Status3xxCount += aggregate.Status3xxCount;
                existing.Status4xxCount += aggregate.Status4xxCount;
                existing.Status5xxCount += aggregate.Status5xxCount;
                existing.LastSeenUtc = nowUtc;
            }
            else
            {
                var entity = new LogIpDailyStatEntity
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
                };
                db.LogIpDailyStats.Add(entity);
                existingByKey.Add((entity.LogDate, entity.Ip), entity);
            }

            if (reportProgressAsync is not null)
            {
                await reportProgressAsync(index + 1, rows.Count);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}
