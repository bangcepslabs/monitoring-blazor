using System.Text;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class IisLogPreviewServiceTests
{
    [Fact]
    public async Task ParseFilesAsync_CanSkipTheMemoryPreviewForBatchWorkspaces()
    {
        const string content = "#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status time-taken\nbad-date 01:02:03 10.0.0.2 GET /batch - 80 - 192.168.0.5 - - 200 0 0 1\n";
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"iis-log-batch-test-{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Monitoring:DataDirectory"] = dataDirectory })
            .Build();
        var store = new IisLogWorkspaceStore(configuration);
        var parser = new IisLogPreviewService(store);

        try
        {
            var file = new MemoryBrowserFile("iis.log", Encoding.UTF8.GetBytes(content));
            var result = await parser.ParseFilesAsync(
                [file],
                file.Size,
                new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
                (_, _) => Task.CompletedTask,
                CancellationToken.None,
                retainPreviewRows: false,
                retainAggregates: false);

            Assert.Empty(result.PreviewRows);
            Assert.Empty(result.Aggregates);
            Assert.True(result.IsPreviewTruncated);
            Assert.Equal(1, result.TotalRows);
            Assert.Equal(1, await store.GetCountAsync(result.WorkspaceId, new IisLogFilterCriteria()));
            List<Monitoring.Blazor.Models.LogIpAggregate>? storedBatch = null;
            await foreach (var batch in store.ReadAggregateBatchesAsync(result.WorkspaceId))
            {
                storedBatch = batch;
                break;
            }
            var storedAggregate = Assert.Single(storedBatch!);
            Assert.Equal("192.168.0.5", storedAggregate.Ip);
            Assert.Equal(new DateOnly(2026, 10, 6), storedAggregate.LogDate);
            await store.DeleteAsync(result.WorkspaceId);
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ParseFilesAsync_CapsPreviewButAggregatesEveryParsedRow()
    {
        const int rowCount = IisLogPreviewService.MaxPreviewRows + 1;
        var content = new StringBuilder("#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) cs(Referer) sc-status sc-substatus sc-win32-status time-taken\n");
        for (var index = 0; index < rowCount; index++)
        {
            content.AppendLine(index == rowCount - 1
                ? "2026-10-06 01:02:03 10.0.0.2 POST /only-after-preview - 80 - 203.0.113.77 unique-search-token unique-referrer 500 0 0 1"
                : "2026-10-06 01:02:03 10.0.0.2 GET /health - 80 - 192.168.0.5 - - 200 0 0 1");
        }

        var dataDirectory = Path.Combine(Path.GetTempPath(), $"iis-log-preview-test-{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Monitoring:DataDirectory"] = dataDirectory })
            .Build();
        var store = new IisLogWorkspaceStore(configuration);
        var parser = new IisLogPreviewService(store);

        try
        {
            var file = new MemoryBrowserFile("iis.log", Encoding.UTF8.GetBytes(content.ToString()));
            var result = await parser.ParseFilesAsync(
                [file],
                file.Size,
                new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
                (_, _) => Task.CompletedTask,
                CancellationToken.None);

            Assert.Equal(rowCount, result.TotalRows);
            Assert.Equal(IisLogPreviewService.MaxPreviewRows, result.PreviewRows.Count);
            Assert.True(result.IsPreviewTruncated);
            Assert.True(store.Exists(result.WorkspaceId));
            var aggregate = Assert.Single(result.Aggregates, item => item.Ip == "192.168.0.5");
            Assert.Equal(rowCount - 1, aggregate.RequestCount);
            Assert.Equal(rowCount - 1, aggregate.Status2xxCount);
            Assert.Equal(2, await store.GetAggregateGroupCountAsync(result.WorkspaceId));
            var storedAggregateBatches = new List<Monitoring.Blazor.Models.LogIpAggregate>();
            await foreach (var batch in store.ReadAggregateBatchesAsync(result.WorkspaceId, batchSize: 1))
            {
                storedAggregateBatches.AddRange(batch);
            }
            var storedRareAggregate = Assert.Single(storedAggregateBatches, item => item.Ip == "203.0.113.77");
            Assert.Equal(1, storedRareAggregate.RequestCount);
            Assert.Equal(1, storedRareAggregate.Status5xxCount);

            var criteria = new IisLogFilterCriteria { StatusGroup = "2", Ip = "192.168.0.5" };
            var allCount = await store.GetCountAsync(result.WorkspaceId, criteria);
            var lastPage = await store.GetPageAsync(result.WorkspaceId, criteria, [], 2_001, 1, CancellationToken.None);
            var summary = await store.GetSummaryAsync(result.WorkspaceId, criteria);
            Assert.Equal(rowCount - 1, allCount);
            Assert.Equal(rowCount - 1, summary.TotalRows);
            Assert.Equal(1, summary.UniqueIps);
            Assert.Equal(1, summary.UniqueUrls);
            Assert.Single(summary.StatusCounts);
            Assert.Equal(rowCount - 1, Assert.Single(summary.TopIps).Value);
            Assert.Single(lastPage);

            var beyondPreviewCriteria = new IisLogFilterCriteria { TextQuery = "unique-search-token" };
            Assert.Equal(1, await store.GetCountAsync(result.WorkspaceId, beyondPreviewCriteria));
            var beyondPreviewPage = await store.GetPageAsync(result.WorkspaceId, beyondPreviewCriteria, [], 1, 25, CancellationToken.None);
            Assert.Equal("203.0.113.77", Assert.Single(beyondPreviewPage).Ip);

            var fieldCriteria = new IisLogFilterCriteria
            {
                Method = "POST",
                Uri = "/only-after-preview",
                Status = "500",
                UserAgent = "unique-search-token",
                Referrer = "unique-referrer"
            };
            Assert.Equal(1, await store.GetCountAsync(result.WorkspaceId, fieldCriteria));
            var fieldFilteredRow = Assert.Single(await store.GetPageAsync(result.WorkspaceId, fieldCriteria, [], 1, 25, CancellationToken.None));
            Assert.Equal("POST", fieldFilteredRow.Method);
            Assert.Equal("/only-after-preview", fieldFilteredRow.Uri);

            await using var csv = new MemoryStream();
            await store.WriteCsvAsync(result.WorkspaceId, criteria, [], csv, CancellationToken.None);
            var exportedLines = Encoding.UTF8.GetString(csv.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(rowCount, exportedLines.Length);
            await store.DeleteAsync(result.WorkspaceId);
            Assert.False(store.Exists(result.WorkspaceId));
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private sealed class MemoryBrowserFile(string name, byte[] content) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => content.LongLength;
        public string ContentType => "text/plain";

        public Stream OpenReadStream(long maxAllowedSize = 512_000, CancellationToken cancellationToken = default)
        {
            if (Size > maxAllowedSize)
            {
                throw new IOException("Test file exceeds the configured maximum size.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new MemoryStream(content, writable: false);
        }
    }
}
