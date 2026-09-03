using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class ApiMetricsStateTests
{
    [Fact]
    public void Record_AggregatesRequestsErrorsAndAverageDuration()
    {
        var metrics = new ApiMetricsState();

        metrics.Record("/api/monitor/all", 200, 10);
        metrics.Record("/api/monitor/all", 500, 30);

        var snapshot = Assert.Single(metrics.GetSnapshot());
        Assert.Equal(2, snapshot.Requests);
        Assert.Equal(1, snapshot.Errors);
        Assert.Equal(20, snapshot.AverageMs);
    }

    [Fact]
    public void GetSnapshot_OrdersEndpointsByRequestCount()
    {
        var metrics = new ApiMetricsState();

        metrics.Record("/api/one", 200, 1);
        metrics.Record("/api/two", 200, 1);
        metrics.Record("/api/two", 200, 1);

        var snapshot = metrics.GetSnapshot();
        Assert.Equal("/api/two", snapshot[0].Path);
        Assert.Equal("/api/one", snapshot[1].Path);
    }
}
