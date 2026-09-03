using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class AgentKeyValidatorTests
{
    [Fact]
    public void IsValid_AcceptsMatchingKey()
    {
        Assert.True(AgentKeyValidator.IsValid("agent-secret", "agent-secret"));
    }

    [Theory]
    [InlineData(null, "agent-secret")]
    [InlineData("agent-secret", null)]
    [InlineData("agent-secret", "wrong-secret")]
    [InlineData("", "agent-secret")]
    public void IsValid_RejectsMissingOrIncorrectKey(string? configured, string? supplied)
    {
        Assert.False(AgentKeyValidator.IsValid(configured, supplied));
    }
}
