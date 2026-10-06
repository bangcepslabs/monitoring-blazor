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

    [Fact]
    public void ParseLines_CanCarryIisFieldsHeaderAcrossBatches()
    {
        string[]? fieldOrder = null;
        var header = "#Fields: date time c-ip cs-username s-ip s-port cs-method cs-uri-stem cs-uri-query sc-status sc-win32-status sc-bytes cs-bytes time-taken cs-host cs(Referer) cs(User-Agent)";
        var first = ApacheLogParser.ParseLines([header], ref fieldOrder);
        var second = ApacheLogParser.ParseLines([
            "2026-09-03 12:00:00 10.0.0.1 - 10.0.0.2 443 GET /api status=ok 200 0 123 456 12 example.test - TestAgent"
        ], ref fieldOrder);

        Assert.Empty(first);
        var row = Assert.Single(second);
        Assert.Equal("10.0.0.1", row.Ip);
        Assert.Equal("/api?status=ok", row.Uri);
        Assert.Equal("200", row.Status);
        Assert.Equal("TestAgent", row.UserAgent);
    }

    [Fact]
    public void ParseLines_AcceptsBomAndTabDelimitedIisLogs()
    {
        var rows = ApacheLogParser.ParseLines([
            "\uFEFF#Fields:\tdate\ttime\tc-ip\tcs-method\tcs-uri-stem\tcs-uri-query\tsc-status\tcs(User-Agent)",
            "2026-09-03\t12:00:00\t10.0.0.2\tGET\t/health\t-\t200\tTestAgent"
        ]);

        var row = Assert.Single(rows);
        Assert.Equal("10.0.0.2", row.Ip);
        Assert.Equal("GET", row.Method);
        Assert.Equal("/health", row.Uri);
        Assert.Equal("200", row.Status);
        Assert.Equal("TestAgent", row.UserAgent);
    }
}
