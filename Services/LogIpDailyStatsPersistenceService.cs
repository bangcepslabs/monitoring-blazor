using Microsoft.EntityFrameworkCore;
using Monitoring.Blazor.Models;
using System.Linq.Expressions;

namespace Monitoring.Blazor.Services;

public static class LogIpDailyStatsPersistenceService
{
    public static async Task<int> AddAggregatesInBatchesAsync(
        MonitoringDbContext db,
        string serverName,
        IAsyncEnumerable<List<LogIpAggregate>> aggregateBatches,
        long totalGroups,
        DateTime nowUtc,
        Func<int, int, Task>? reportProgressAsync = null,
        CancellationToken cancellationToken = default)
    {
        var processed = 0L;
        var progressTotal = (int)Math.Clamp(totalGroups, 1, int.MaxValue);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await foreach (var batch in aggregateBatches.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (batch.Count == 0) continue;

            var parameter = Expression.Parameter(typeof(LogIpDailyStatEntity), "row");
            var keyMatches = new List<Expression>(batch.Count);
            foreach (var aggregate in batch)
            {
                var dateMatch = Expression.Equal(
                    Expression.Property(parameter, nameof(LogIpDailyStatEntity.LogDate)),
                    Expression.Constant(aggregate.LogDate));
                var ipMatch = Expression.Equal(
                    Expression.Property(parameter, nameof(LogIpDailyStatEntity.Ip)),
                    Expression.Constant(aggregate.Ip));
                keyMatches.Add(Expression.AndAlso(dateMatch, ipMatch));
            }

            var keyFilter = CombineBalancedOr(keyMatches, 0, keyMatches.Count);
            var predicate = Expression.Lambda<Func<LogIpDailyStatEntity, bool>>(keyFilter, parameter);
            var existingRows = await db.LogIpDailyStats
                .Where(row => row.ServerName == serverName)
                .Where(predicate)
                .ToListAsync(cancellationToken);
            var existingByKey = existingRows.ToDictionary(row => (row.LogDate, row.Ip));

            foreach (var aggregate in batch)
            {
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
            }

            await db.SaveChangesAsync(cancellationToken);
            processed += batch.Count;
            db.ChangeTracker.Clear();
            if (reportProgressAsync is not null)
            {
                await reportProgressAsync((int)Math.Min(processed, progressTotal), progressTotal);
            }
        }

        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return processed > int.MaxValue ? int.MaxValue : (int)processed;
    }

    private static Expression CombineBalancedOr(IReadOnlyList<Expression> expressions, int start, int count)
    {
        if (count == 1) return expressions[start];
        var leftCount = count / 2;
        return Expression.OrElse(
            CombineBalancedOr(expressions, start, leftCount),
            CombineBalancedOr(expressions, start + leftCount, count - leftCount));
    }

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
