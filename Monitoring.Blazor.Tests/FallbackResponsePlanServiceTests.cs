using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class FallbackResponsePlanServiceTests
{
    [Fact]
    public void Build_ReturnsGeneralGuidanceWhenThereIsNoSpecificEvidence()
    {
        var result = FallbackResponsePlanService.Build(new(false, false, false, false, false));

        Assert.Equal("공격 의심 URL과 IP를 차단·속도 제한·모니터링 대상으로 구분해 단계적으로 대응하세요.", result);
    }

    [Fact]
    public void Build_IncludesEvidenceBasedGuidanceInStableOrder()
    {
        var result = FallbackResponsePlanService.Build(new(true, true, true, true, true));

        Assert.Contains("분산 접근 후보 URL", result);
        Assert.Contains("정상 응답(200)", result);
        Assert.Contains("404 스캔 IP", result);
        Assert.Contains("의심스러운 쿼리 문자열", result);
        Assert.Contains("게시판·조회 페이지", result);
        Assert.True(result.IndexOf("분산 접근 후보 URL", StringComparison.Ordinal) < result.IndexOf("404 스캔 IP", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_EnglishReportUsesEnglishForEveryEvidenceBranch()
    {
        var result = FallbackResponsePlanService.Build(new(true, true, true, true, true), isKorean: false);
        Assert.DoesNotMatch("[가-힣]", result);
        Assert.Contains("distributed URL target", result);
        Assert.Contains("successful (200)", result);
        Assert.Contains("404-scanning", result);
        Assert.Contains("suspicious query strings", result);
        Assert.Contains("board or lookup pages", result);
    }
}
