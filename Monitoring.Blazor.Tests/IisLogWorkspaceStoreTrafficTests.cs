using Microsoft.Extensions.Configuration;
using Monitoring.Blazor.Models;
using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class IisLogWorkspaceStoreTrafficTests
{
    [Fact]
    public async Task GetTopTrafficPathsAsync_GroupsQueryVariantsAcrossFullWorkspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"iis-path-traffic-{Guid.NewGuid():N}");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Monitoring:DataDirectory"] = directory })
            .Build();
        var store = new IisLogWorkspaceStore(configuration);
        try
        {
            await using var writer = await store.CreateAsync(0, DateTime.UtcNow, CancellationToken.None);
            await writer.AppendAsync(
            [
                new ParsedLogRow { Date = "2026-11-16", Time = "01:00:00", Uri = "/board/inform.aspx?id=1", Ip = "192.0.2.1", Status = "200" },
                new ParsedLogRow { Date = "2026-11-16", Time = "01:00:01", Uri = "/board/inform.aspx?id=2", Ip = "192.0.2.2", Status = "404" },
                new ParsedLogRow { Date = "2026-11-16", Time = "01:00:02", Uri = "/health", Ip = "192.0.2.1", Status = "200" }
            ], CancellationToken.None);
            await writer.CompleteAsync(CancellationToken.None);

            var paths = await store.GetTopTrafficPathsAsync(writer.Id, new IisLogFilterCriteria());
            var board = paths[0];
            Assert.Equal("/board/inform.aspx", board.Path);
            Assert.Equal(2, board.Requests);
            Assert.Equal(2, board.UniqueIps);
            Assert.Equal(1, board.ErrorResponses);
            Assert.Equal(2, board.UriVariants);
            Assert.Single(await store.GetTopTrafficPathsAsync(writer.Id, new IisLogFilterCriteria { StatusCode = "404" }));
            await store.DeleteAsync(writer.Id);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
