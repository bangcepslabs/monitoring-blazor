namespace Monitoring.Blazor.Services;

public sealed class LanguageState
{
    private string _language = "ko";
    private readonly Dictionary<string, (string Korean, string English)> _messages = new(StringComparer.Ordinal);
    private readonly Queue<string> _messageKeys = new();

    public event Action? Changed;

    public string Code => _language;

    public bool IsKorean => string.Equals(_language, "ko", StringComparison.OrdinalIgnoreCase);

    public void SetLanguage(string? language)
    {
        var next = string.Equals(language, "ko", StringComparison.OrdinalIgnoreCase) ? "ko" : "en";
        if (string.Equals(_language, next, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _language = next;
        Changed?.Invoke();
    }

    public string T(string korean, string english)
    {
        lock (_messages)
        {
            Remember(korean, korean, english);
            Remember(english, korean, english);
        }
        return IsKorean ? korean : english;
    }

    public string Label(string? value) => value switch
    {
        "Agent recovered" => T("에이전트 연결 복구", "Agent recovered"),
        "Agent offline" => T("에이전트 연결 끊김", "Agent offline"),
        "RAM" => T("메모리", "Memory"),
        "DISK" => T("디스크", "Disk"),
        "log_analysis_runtime_update" => T("로그 분석 규칙 변경", "Log analysis rules updated"),
        "log_import_runtime_update" => T("로그 수집 설정 변경", "Log import settings updated"),
        "log_export_runtime_update" => T("로그 내보내기 설정 변경", "Log export settings updated"),
        "ollama_runtime_update" => T("AI 분석 설정 변경", "AI analysis settings updated"),
        "alert_acknowledge_undo" => T("알림 확인 취소", "Alert acknowledgement removed"),
        "Admin" => T("관리자", "Administrator"),
        "User" => T("사용자", "User"),
        "Alert" => T("알림", "Alert"),
        "Login" or "login" => T("로그인", "Sign-in"),
        "Audit" => T("감사 기록", "Audit"),
        "Success" or "success" => T("성공", "Success"),
        "Failed" => T("실패", "Failed"),
        "Threshold" => T("임계값 초과", "Threshold exceeded"),
        "Recovery" => T("정상 복구", "Recovery"),
        "Anomaly" => T("이상 징후", "Anomaly"),
        "invalid_password" => T("비밀번호 불일치", "Incorrect password"),
        "user_not_found" or "not_found" => T("계정 없음", "Account not found"),
        "inactive" => T("비활성 계정", "Inactive account"),
        "locked" or "manual_lock" => T("계정 잠김", "Account locked"),
        "email_unverified" => T("이메일 미인증", "Email not verified"),
        "logout" => T("로그아웃", "Sign-out"),
        "member_create" => T("계정 생성", "Account created"),
        "member_register" => T("회원가입", "Registration"),
        "member_update" => T("회원 정보 변경", "Member updated"),
        "member_role" => T("권한 변경", "Role changed"),
        "member_active" => T("계정 활성 상태 변경", "Account status changed"),
        "member_display_name" => T("표시 이름 변경", "Display name changed"),
        "password_change" or "password_change_self" => T("비밀번호 변경", "Password changed"),
        "password_reset_request" => T("비밀번호 재설정 요청", "Password reset requested"),
        "password_reset_confirm" => T("비밀번호 재설정", "Password reset"),
        "two_factor" or "two_factor_verify" => T("2차 인증", "Two-factor verification"),
        "two_factor_setup_confirm" => T("2차 인증 설정", "Two-factor setup"),
        "two_factor_recovery" => T("복구 코드 인증", "Recovery code verification"),
        "settings_backup" => T("설정 백업", "Settings backup"),
        "settings_restore" => T("설정 복원", "Settings restored"),
        "alert_settings_update" => T("알림 규칙 변경", "Alert rules updated"),
        "security_settings_update" => T("보안 설정 변경", "Security settings updated"),
        "email_settings_update" => T("이메일 설정 변경", "Email settings updated"),
        "notification_settings_update" => T("알림 채널 변경", "Notification channels updated"),
        "sql_target_update" => T("SQL 연결 대상 변경", "SQL target updated"),
        "sql_target_remove" => T("SQL 연결 대상 삭제", "SQL target removed"),
        _ => value ?? string.Empty
    };

    // Only opt-in UI messages are translated. Log data, names and identifiers remain intact.
    public string? Localize(string? message)
    {
        lock (_messages)
        {
            return message is not null && _messages.TryGetValue(message, out var pair)
                ? (IsKorean ? pair.Korean : pair.English)
                : message;
        }
    }

    private void Remember(string key, string korean, string english)
    {
        if (_messages.ContainsKey(key)) return;
        while (_messages.Count >= 2048) _messages.Remove(_messageKeys.Dequeue());
        _messages[key] = (korean, english);
        _messageKeys.Enqueue(key);
    }
}
