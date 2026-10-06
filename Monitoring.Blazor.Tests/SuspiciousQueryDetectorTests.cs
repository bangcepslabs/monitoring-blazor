using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class SuspiciousQueryDetectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/health")]
    [InlineData("/search?q=normal")]
    public void Detect_ReturnsNullForMissingOrBenignQuery(string? uri)
    {
        Assert.Null(SuspiciousQueryDetector.Detect(uri));
    }

    [Fact]
    public void Detect_FindsDoubleEncodedTraversalAndKeepsOriginalQueryWithoutFragment()
    {
        var match = SuspiciousQueryDetector.Detect("/download?path=%252e%252e%252fsecret#ignored");

        Assert.NotNull(match);
        Assert.Equal("path=%252e%252e%252fsecret", match.QueryString);
        Assert.Contains("traversal_or_encoded_path", match.Reasons);
    }

    [Fact]
    public void Detect_FindsSqlInjectionAndExcessiveParameters()
    {
        var match = SuspiciousQueryDetector.Detect("/search?q=union%20select&id=1&a=1&b=2&c=3&d=4&e=5&f=6");

        Assert.NotNull(match);
        Assert.Contains("sqli_pattern", match.Reasons);
        Assert.Contains("many_parameters", match.Reasons);
    }

    [Fact]
    public void Detect_FlagsLongQuery()
    {
        var match = SuspiciousQueryDetector.Detect("/search?q=" + new string('a', 120));

        Assert.NotNull(match);
        Assert.Contains("long_query", match.Reasons);
    }
}
