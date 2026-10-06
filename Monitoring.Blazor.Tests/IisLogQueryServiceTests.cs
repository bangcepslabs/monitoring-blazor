using Monitoring.Blazor.Models;
using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class IisLogQueryServiceTests
{
    [Fact]
    public void FilterAndSort_AppliesSearchFieldsAndExcludedIpTokens()
    {
        var rows = new[]
        {
            Row("10.0.0.1", "GET", "/api/users", "500", "Browser"),
            Row("10.0.0.2", "POST", "/api/users", "500", "Browser"),
            Row("10.0.0.3", "GET", "/health", "200", "Browser")
        };

        var result = IisLogQueryService.FilterAndSort(rows, new IisLogFilterCriteria
        {
            StatusGroup = "5",
            UrlQuery = "/api",
            MethodQuery = "GET",
            ExcludeIpQuery = "10.0.0.2, 192.168.1.1"
        }, []);

        var row = Assert.Single(result);
        Assert.Equal("10.0.0.1", row.Ip);
    }

    [Fact]
    public void FilterAndSort_AppliesMultipleSortsInPriorityOrder()
    {
        var rows = new[]
        {
            Row("10.0.0.2", "GET", "/z", "200", "a"),
            Row("10.0.0.1", "GET", "/b", "200", "b"),
            Row("10.0.0.3", "POST", "/a", "200", "c")
        };
        var sorts = new List<LogRowSort>
        {
            new(nameof(ParsedLogRow.Method), ascending: true),
            new(nameof(ParsedLogRow.Ip), ascending: false)
        };

        var result = IisLogQueryService.FilterAndSort(rows, new IisLogFilterCriteria(), sorts);

        Assert.Equal(["10.0.0.2", "10.0.0.1", "10.0.0.3"], result.Select(row => row.Ip));
    }

    private static ParsedLogRow Row(string ip, string method, string uri, string status, string userAgent) => new()
    {
        Date = "2026-10-05",
        Time = "12:00:00",
        Ip = ip,
        Method = method,
        Uri = uri,
        Status = status,
        Referrer = string.Empty,
        UserAgent = userAgent
    };
}
