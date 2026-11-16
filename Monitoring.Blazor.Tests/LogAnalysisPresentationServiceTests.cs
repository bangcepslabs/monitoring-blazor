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

    [Fact]
    public void GetPreviewLines_CondensesIpRowsAndResponsePlan()
    {
        var ips = new AnalysisSection("의심 IP");
        ips.Lines.Add("- 34.56.201.231 · 차단 검토 · 점수 180 · 요청 118 · 주요 URL /very/long/path");
        ips.Lines.Add("의심 쿼리 문자열: " + new string('x', 200));
        Assert.Equal("34.56.201.231 · 차단 검토 · 점수 180 · 요청 118", Assert.Single(LogAnalysisPresentationService.GetPreviewLines(ips)));

        var plan = new AnalysisSection("대응 방안");
        plan.Lines.Add("URL 단위 속도 제한을 적용하세요. IP 차단을 검토하세요. 서버 로그를 점검하세요. 추가 근거를 확인하세요.");
        Assert.Equal(3, LogAnalysisPresentationService.GetPreviewLines(plan).Count);
        Assert.Equal("URL 단위 속도 제한을 적용하세요.", LogAnalysisPresentationService.GetPreviewLines(plan)[0]);
    }

    [Fact]
    public void GetPreviewLines_ShowsEveryIpCandidate()
    {
        var section = new AnalysisSection("의심 IP");
        for (var index = 1; index <= 7; index++) section.Lines.Add($"- 192.0.2.{index} · 차단 검토 · 점수 100 · 요청 50 · 주요 URL /board");
        Assert.Equal(7, LogAnalysisPresentationService.GetPreviewLines(section).Count);
    }

    [Fact]
    public void ParseBlockCandidateIps_ExcludesRateLimitAndMonitorIps()
    {
        const string report = """
            [의심 IP]
            - 192.0.2.1 · 차단 검토 · 점수 180
            - 192.0.2.2 · 속도 제한 검토 · 점수 80
            [대응 후보]
            차단 검토: 192.0.2.3(Block), 192.0.2.1(Block)
            속도 제한 검토: 192.0.2.4(RateLimit)
            모니터링: 192.0.2.5
            """;
        Assert.Equal(["192.0.2.1", "192.0.2.3"], LogAnalysisPresentationService.ParseBlockCandidateIps(report));
        Assert.Equal(["192.0.2.6"], LogAnalysisPresentationService.ParseBlockCandidateIps("[차단 후보]\n즉시 차단: 192.0.2.6; 속도 제한: 192.0.2.7"));
    }

    [Fact]
    public void SummarizePaths_GroupsQueryVariants()
    {
        var rows = new[]
        {
            new Monitoring.Blazor.Models.ParsedLogRow { Uri = "/board/inform.aspx?id=1", Ip = "192.0.2.1", Status = "200" },
            new Monitoring.Blazor.Models.ParsedLogRow { Uri = "/board/inform.aspx?id=2", Ip = "192.0.2.2", Status = "404" },
            new Monitoring.Blazor.Models.ParsedLogRow { Uri = "/health", Ip = "192.0.2.1", Status = "200" }
        };
        var path = LogAnalysisPresentationService.SummarizePaths(rows)[0];
        Assert.Equal("/board/inform.aspx", path.Path);
        Assert.Equal(2, path.Requests);
        Assert.Equal(2, path.UniqueIps);
        Assert.Equal(1, path.ErrorResponses);
        Assert.Equal(2, path.UriVariants);
    }
}
