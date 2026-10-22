namespace Monitoring.Blazor.Services;

/// <summary>Presentation-only translations; diagnostic values stay unchanged.</summary>
public static class DependencyStatusText
{
    public static string Name(string value, LanguageState language) => value switch
    {
        "Database" => language.T("데이터베이스", "Database"),
        "Notifications" => language.T("알림 채널", "Notifications"),
        "Paths" => language.T("저장 경로", "Paths"),
        "SQL Targets" => language.T("SQL 연결 대상", "SQL targets"),
        _ => value
    };

    public static string Status(string value, LanguageState language) => value switch
    {
        "Healthy" => language.T("정상", "Healthy"),
        "Unavailable" => language.T("연결 불가", "Unavailable"),
        "Error" => language.T("오류", "Error"),
        "Disabled" => language.T("사용 안 함", "Disabled"),
        "Configured" => language.T("설정 완료", "Configured"),
        "Needs attention" => language.T("확인 필요", "Needs attention"),
        "Ready" => language.T("준비됨", "Ready"),
        "Missing" => language.T("설정 필요", "Missing"),
        _ => value
    };

    public static string Details(DependencyStatusEntry entry, LanguageState language)
    {
        var value = entry.Details;
        if (entry.Name == "Paths" && value.StartsWith("Log: ", StringComparison.Ordinal))
        {
            var parts = value[5..].Split(" | Output: ", 2, StringSplitOptions.None);
            if (parts.Length == 2) return language.T($"로그: {parts[0]} | 내보내기: {parts[1]}", value);
        }
        const string targetSuffix = " target(s) configured.";
        if (entry.Name == "SQL Targets" && value.EndsWith(targetSuffix, StringComparison.Ordinal)
            && int.TryParse(value[..^targetSuffix.Length], out var count))
        {
            return language.T($"연결 대상 {count}개가 설정되어 있습니다.", $"{count} SQL target{(count == 1 ? " is" : "s are")} configured.");
        }
        return value switch
        {
            "Connected to MonitoringDb." => language.T("모니터링 데이터베이스에 연결되었습니다.", value),
            "Cannot connect to MonitoringDb." => language.T("모니터링 데이터베이스에 연결할 수 없습니다.", value),
            "No Ollama base URL configured." => language.T("Ollama 서버 주소를 설정하세요.", value),
            "Email/SMTP settings are incomplete." => language.T("이메일 발송을 위한 SMTP 설정을 확인하세요.", "Check the SMTP settings for email delivery."),
            "Enable at least one notification channel." => language.T("알림 채널을 하나 이상 활성화하세요.", value),
            "No SQL target entries configured." => language.T("설정된 SQL 연결 대상이 없습니다.", value),
            _ => value
        };
    }
}
