namespace Monitoring.Blazor.Services;

public sealed record FallbackResponsePlanEvidence(
    bool HasDistributedDdosCandidate,
    bool HasConcentratedSuccessfulTraffic,
    bool HasNotFoundScanning,
    bool HasSuspiciousQueries,
    bool HasRepeatedBoardAccess);

public static class FallbackResponsePlanService
{
    public static string Build(FallbackResponsePlanEvidence evidence, bool isKorean = true)
    {
        string T(string korean, string english) => isKorean ? korean : english;
        var actions = new List<string>();

        if (evidence.HasDistributedDdosCandidate)
            actions.Add(T("분산 접근 후보 URL은 특정 IP 차단만으로는 효과가 제한적일 수 있으므로 URL 단위 속도 제한, 캐싱, CDN/WAF 보호를 우선 적용하세요.", "Blocking individual IPs may have limited effect on a distributed URL target. Prioritize URL-based rate limiting, caching, and CDN/WAF protection."));
        if (evidence.HasConcentratedSuccessfulTraffic)
            actions.Add(T("정상 응답(200)을 반복 요청하는 상위 IP가 특정 URL에 집중되어 있다면 스크래핑 가능성이 있습니다. 해당 IP 차단과 함께 URL별 요청 빈도를 제한하세요.", "Repeated successful (200) requests concentrated on a URL may indicate scraping. Consider blocking the source IP and rate-limiting requests to that URL."));
        if (evidence.HasNotFoundScanning)
            actions.Add(T("404 스캔 IP는 관리자 페이지·백업 파일·설정 파일 탐색 여부를 확인해 우선 차단을 검토하세요. 반복 패턴이 뚜렷하지 않으면 속도 제한부터 단계적으로 적용하세요.", "Review 404-scanning IPs for probes targeting admin pages, backups, or configuration files. Consider blocking them; if the pattern is weak, start with rate limiting."));
        if (evidence.HasSuspiciousQueries)
            actions.Add(T("의심스러운 쿼리 문자열은 애플리케이션 및 WAF 로그와 대조해 실제 취약점 탐색인지 확인하세요.", "Compare suspicious query strings with application and WAF logs to determine whether they indicate vulnerability probing."));
        if (evidence.HasRepeatedBoardAccess)
            actions.Add(T("게시판·조회 페이지에 반복 요청이 집중되면 robots 및 캐시 정책, 세션·쿠키 검증, URL별 요청 제한을 함께 점검하세요.", "If requests are concentrated on board or lookup pages, review robots and caching policies, session/cookie validation, and per-URL rate limits."));

        if (actions.Count == 0)
            actions.Add(T("공격 의심 URL과 IP를 차단·속도 제한·모니터링 대상으로 구분해 단계적으로 대응하세요.", "Use the most-targeted URLs and suspicious IPs to prioritize blocking, rate limiting, and monitoring."));

        return string.Join(" ", actions);
    }
}
