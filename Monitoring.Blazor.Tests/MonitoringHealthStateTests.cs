using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class MonitoringHealthStateTests
{
    [Fact]
    public void MarkSuccess_RecordsLastSuccessAndClearsNoData()
    {
        var state = new MonitoringHealthState();

        state.MarkSuccess("server-monitor");

        var snapshot = state.Get("server-monitor");
        Assert.NotNull(snapshot.LastSuccessUtc);
        Assert.Null(snapshot.LastFailureUtc);
        Assert.Null(snapshot.LastError);
    }

    [Fact]
    public void MarkFailure_RecordsErrorWithoutLosingLastSuccess()
    {
        var state = new MonitoringHealthState();
        state.MarkSuccess("server-monitor");
        state.MarkFailure("server-monitor", new InvalidOperationException("collector unavailable"));

        var snapshot = state.Get("server-monitor");
        Assert.NotNull(snapshot.LastSuccessUtc);
        Assert.NotNull(snapshot.LastFailureUtc);
        Assert.Equal("collector unavailable", snapshot.LastError);
    }
}
