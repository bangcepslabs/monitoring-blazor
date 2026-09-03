using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class LogParserTests
{
    [Fact]
    public void ParseLines_UsesFieldsHeaderAndCombinesQueryString()
    {
        var rows = ApacheLogParser.ParseLines([
            "#Fields: date time c-ip cs-method cs-uri-stem cs-uri-query sc-status cs(Referer) cs(User-Agent)",
            "2026-09-03 12:00:00 10.0.0.1 GET /health status=ok 200 - OpsEye"
        ]);

        var row = Assert.Single(rows);
        Assert.Equal("10.0.0.1", row.Ip);
        Assert.Equal("GET", row.Method);
        Assert.Equal("/health?status=ok", row.Uri);
        Assert.Equal("200", row.Status);
    }

    [Fact]
    public void ParseLines_IgnoresCommentsAndBlankLines()
    {
        var rows = ApacheLogParser.ParseLines(["", "# comment", "#Fields: date time c-ip", "2026-09-03 12:00:00"]);

        Assert.Empty(rows);
    }
}
