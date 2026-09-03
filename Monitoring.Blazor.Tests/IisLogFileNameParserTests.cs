using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class IisLogFileNameParserTests
{
    [Theory]
    [InlineData("u_ex260903.log", 2026, 9, 3)]
    [InlineData("ex260903.log", 2026, 9, 3)]
    public void TryGetLogDate_ParsesSupportedNames(string fileName, int year, int month, int day)
    {
        Assert.True(IisLogFileNameParser.TryGetLogDate(fileName, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Fact]
    public void TryGetLogDate_RejectsUnknownName()
    {
        Assert.False(IisLogFileNameParser.TryGetLogDate("access.log", out _));
    }
}
