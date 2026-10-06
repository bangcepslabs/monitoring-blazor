namespace Monitoring.Blazor.Services;

public sealed record FallbackResponsePlanEvidence(
    bool HasDistributedDdosCandidate,
    bool HasConcentratedSuccessfulTraffic,
    bool HasNotFoundScanning,
    bool HasSuspiciousQueries,
    bool HasRepeatedBoardAccess);

public static class FallbackResponsePlanService
{
    public static string Build(FallbackResponsePlanEvidence evidence)
    {
        var actions = new List<string>();

        if (evidence.HasDistributedDdosCandidate)
            actions.Add("분산 접근 후보 URL은 특정 IP 차단만으로는 효과가 제한적일 수 있으므로 URL 단위 RateLimit, 캐싱, CDN/WAF 보호를 우선 적용하세요.");
        if (evidence.HasConcentratedSuccessfulTraffic)
            actions.Add("200 정상 응답 기반 반복 호출이 상위 IP에 집중된 URL은 스크래핑 가능성이 높으므로 해당 IP 차단과 함께 대상 URL의 호출 빈도 제어를 병행하세요.");
        if (evidence.HasNotFoundScanning)
            actions.Add("404 스캐닝 IP는 관리자 페이지, 백업 파일, 설정 파일 탐색 여부를 기준으로 우선 차단하고, 반복 패턴이 약한 경우에는 속도 제한으로 단계 대응하세요.");
        if (evidence.HasSuspiciousQueries)
            actions.Add("의심 QueryString 요청은 애플리케이션 로그와 WAF 로그를 함께 대조해 실제 취약점 탐색인지 추가 확인하세요.");
        if (evidence.HasRepeatedBoardAccess)
            actions.Add("게시판/조회성 페이지에 반복 접근이 집중되면 robots 정책, 캐시 정책, 세션·쿠키 검증, URL별 요청 제한을 함께 점검하세요.");

        if (actions.Count == 0)
            actions.Add("상위 공격 URL과 의심 IP를 기준으로 차단, 속도 제한, 모니터링 대상을 나눠 순차 대응하세요.");

        return string.Join(" ", actions);
    }
}
