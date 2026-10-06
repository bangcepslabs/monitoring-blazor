using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class IpRecommendedActionServiceTests
{
    [Theory]
    [InlineData(0, "Allow")]
    [InlineData(20, "Monitor")]
    [InlineData(50, "RateLimit")]
    [InlineData(80, "Block")]
    public void Recommend_UsesScoreThresholds(int score, string expected)
    {
        var action = IpRecommendedActionService.Recommend(new IpRiskEvidence(
            score, 10, 0, 0, 0, false, 0, 0, 0, 0, 0));

        Assert.Equal(expected, action);
    }

    [Fact]
    public void Recommend_PrioritizesSensitivePathProbes()
    {
        var action = IpRecommendedActionService.Recommend(new IpRiskEvidence(
            0, 1, 0, 0, 0, false, 0, 0, 0, 1, 0));

        Assert.Equal("Block", action);
    }

    [Fact]
    public void Recommend_LimitsAdminProbeWithoutError()
    {
        var action = IpRecommendedActionService.Recommend(new IpRiskEvidence(
            0, 1, 0, 0, 0, false, 1, 0, 0, 0, 0));

        Assert.Equal("RateLimit", action);
    }

    [Fact]
    public void Recommend_AllowsLowRiskBotBeforeGenericErrorThresholds()
    {
        var action = IpRecommendedActionService.Recommend(new IpRiskEvidence(
            20, 10, 0, 8, 0, true, 0, 0, 0, 0, 0));

        Assert.Equal("Allow", action);
    }
}
