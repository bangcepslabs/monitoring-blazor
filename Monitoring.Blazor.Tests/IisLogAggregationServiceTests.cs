using Monitoring.Blazor.Models;
using Monitoring.Blazor.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class IisLogAggregationServiceTests
{
    [Fact]
    public void AddRows_MergesBatchesByDateAndIpAndCountsStatusGroups()
    {
        var fallbackUtc = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        var aggregates = new Dictionary<(DateOnly LogDate, string Ip), LogIpAggregate>();

        IisLogAggregationService.AddRows(aggregates,
        [
            Row("2026-10-05", "10.0.0.1", "200"),
            Row("2026-10-05", "10.0.0.1", "302")
        ], fallbackUtc);
        IisLogAggregationService.AddRows(aggregates,
        [
            Row("2026-10-05", "10.0.0.1", "404"),
            Row("2026-10-05", "10.0.0.1", "503"),
            Row("2026-10-05", "10.0.0.2", "not-a-status")
        ], fallbackUtc);

        Assert.Equal(2, aggregates.Count);
        var firstIp = aggregates[(new DateOnly(2026, 10, 5), "10.0.0.1")];
        Assert.Equal(4, firstIp.RequestCount);
        Assert.Equal(1, firstIp.Status2xxCount);
        Assert.Equal(1, firstIp.Status3xxCount);
        Assert.Equal(1, firstIp.Status4xxCount);
        Assert.Equal(1, firstIp.Status5xxCount);

        var secondIp = aggregates[(new DateOnly(2026, 10, 5), "10.0.0.2")];
        Assert.Equal(1, secondIp.RequestCount);
        Assert.Equal(0, secondIp.Status2xxCount + secondIp.Status3xxCount + secondIp.Status4xxCount + secondIp.Status5xxCount);
    }

    [Fact]
    public void CreateAggregateMap_UsesFallbackDateForInvalidLogDate()
    {
        var fallbackUtc = new DateTime(2026, 10, 6, 23, 30, 0, DateTimeKind.Utc);

        var aggregates = IisLogAggregationService.CreateAggregateMap(
            [Row("invalid", "10.0.0.3", "500")],
            fallbackUtc);

        var aggregate = Assert.Single(aggregates).Value;
        Assert.Equal(new DateOnly(2026, 10, 6), aggregate.LogDate);
        Assert.Equal(1, aggregate.Status5xxCount);
    }

    [Fact]
    public async Task AddAggregatesAsync_InsertsThenAccumulatesExistingDailyRows()
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var logDate = new DateOnly(2026, 10, 5);
        var firstWriteUtc = new DateTime(2026, 10, 6, 1, 0, 0, DateTimeKind.Utc);
        var secondWriteUtc = firstWriteUtc.AddHours(1);

        await using var db = new MonitoringDbContext(options);
        var inserted = await LogIpDailyStatsPersistenceService.AddAggregatesAsync(
            db,
            "HOST-01",
            [new LogIpAggregate
            {
                LogDate = logDate,
                Ip = "10.0.0.1",
                RequestCount = 2,
                Status2xxCount = 1,
                Status5xxCount = 1
            }],
            firstWriteUtc);
        var updated = await LogIpDailyStatsPersistenceService.AddAggregatesAsync(
            db,
            "HOST-01",
            [new LogIpAggregate
            {
                LogDate = logDate,
                Ip = "10.0.0.1",
                RequestCount = 3,
                Status4xxCount = 3
            }],
            secondWriteUtc);

        var stored = await db.LogIpDailyStats.SingleAsync();
        Assert.Equal(1, inserted);
        Assert.Equal(1, updated);
        Assert.Equal(5, stored.RequestCount);
        Assert.Equal(1, stored.Status2xxCount);
        Assert.Equal(3, stored.Status4xxCount);
        Assert.Equal(1, stored.Status5xxCount);
        Assert.Equal(firstWriteUtc, stored.FirstSeenUtc);
        Assert.Equal(secondWriteUtc, stored.LastSeenUtc);
    }

    [Fact]
    public async Task AddAggregatesInBatchesAsync_UpsertsBoundedBatchesAndReportsProgress()
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var logDate = new DateOnly(2026, 10, 5);
        var nowUtc = new DateTime(2026, 10, 6, 2, 0, 0, DateTimeKind.Utc);
        var progress = new List<(int Current, int Total)>();

        await using var db = new MonitoringDbContext(options);
        await LogIpDailyStatsPersistenceService.AddAggregatesAsync(
            db,
            "HOST-01",
            [new LogIpAggregate { LogDate = logDate, Ip = "10.0.0.1", RequestCount = 3, Status2xxCount = 3 }],
            nowUtc.AddHours(-1));

        async IAsyncEnumerable<List<LogIpAggregate>> Batches()
        {
            yield return [new LogIpAggregate { LogDate = logDate, Ip = "10.0.0.1", RequestCount = 2, Status5xxCount = 2 }];
            await Task.Yield();
            yield return [new LogIpAggregate { LogDate = logDate, Ip = "10.0.0.2", RequestCount = 1, Status4xxCount = 1 }];
        }

        var processed = await LogIpDailyStatsPersistenceService.AddAggregatesInBatchesAsync(
            db,
            "HOST-01",
            Batches(),
            totalGroups: 2,
            nowUtc,
            (current, total) =>
            {
                progress.Add((current, total));
                return Task.CompletedTask;
            });

        Assert.Equal(2, processed);
        Assert.Equal(2, progress.Count);
        Assert.Equal((1, 2), progress[0]);
        Assert.Equal((2, 2), progress[1]);
        var stored = await db.LogIpDailyStats.OrderBy(row => row.Ip).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(5, stored[0].RequestCount);
        Assert.Equal(3, stored[0].Status2xxCount);
        Assert.Equal(2, stored[0].Status5xxCount);
        Assert.Equal(1, stored[1].Status4xxCount);

    }

    private static ParsedLogRow Row(string date, string ip, string status) => new()
    {
        Date = date,
        Ip = ip,
        Status = status
    };
}
