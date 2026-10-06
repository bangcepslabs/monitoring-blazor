using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class FallbackResponsePlanServiceTests
{
    [Fact]
    public void Build_ReturnsGeneralGuidanceWhenThereIsNoSpecificEvidence()
    {
        var result = FallbackResponsePlanService.Build(new(false, false, false, false, false));

        Assert.Equal("상위 공격 URL과 의심 IP를 기준으로 차단, 속도 제한, 모니터링 대상을 나눠 순차 대응하세요.", result);
    }

    [Fact]
    public void Build_IncludesEvidenceBasedGuidanceInStableOrder()
    {
        var result = FallbackResponsePlanService.Build(new(true, true, true, true, true));

        Assert.Contains("분산 접근 후보 URL", result);
        Assert.Contains("200 정상 응답", result);
        Assert.Contains("404 스캐닝 IP", result);
        Assert.Contains("의심 QueryString", result);
        Assert.Contains("게시판/조회성 페이지", result);
        Assert.True(result.IndexOf("분산 접근 후보 URL", StringComparison.Ordinal) < result.IndexOf("404 스캐닝 IP", StringComparison.Ordinal));
    }
}
