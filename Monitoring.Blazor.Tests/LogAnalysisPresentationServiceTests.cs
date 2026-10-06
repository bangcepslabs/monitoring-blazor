using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class LogAnalysisPresentationServiceTests
{
    [Fact]
    public void ParseDisplayedSections_NormalizesMarkdownAndKeepsReportSections()
    {
        const string report = """
            [전체 요약]
            요청 100건

            ## [공격 URL]
            - /api/search 반복

            **의심 IP**
            - 10.0.0.1

            [위험도]
            높음
            """;

        var sections = LogAnalysisPresentationService.ParseDisplayedSections(report);

        Assert.Equal(["공격 URL", "의심 IP"], sections.Select(section => section.Title));
        Assert.Equal("- /api/search 반복", Assert.Single(sections[0].Lines));
    }

    [Fact]
    public void ParseCandidateIps_ExtractsCommaSeparatedValuesWithoutDuplicates()
    {
        var candidates = LogAnalysisPresentationService.ParseCandidateIps(
            "[차단 후보]\r\n즉시 차단: 10.0.0.1, 10.0.0.2, 10.0.0.1\r\n속도 제한: 10.0.0.3",
            "즉시 차단");

        Assert.Equal(["10.0.0.1", "10.0.0.2"], candidates);
        Assert.Empty(LogAnalysisPresentationService.ParseCandidateIps("즉시 차단: 없음", "즉시 차단"));
    }
}
