(function () {
    const dictionary = {
        "Overview": "개요", "Operations": "운영", "Analysis": "분석", "Dashboard": "대시보드",
        "Server Groups": "서버 그룹", "Server Compare": "서버 비교", "Event Timeline": "이벤트 타임라인",
        "Incident Timeline": "인시던트 타임라인", "Alert History": "알림 이력", "Dependencies": "의존 서비스",
        "System Status": "시스템 상태", "IIS Log Parser": "IIS 로그 파서", "Java Log Parser": "Java 로그 파서",
        "IP Analysis": "IP 분석", "User Area": "사용자 영역", "Guest Area": "게스트 영역", "Admin Area": "관리자 영역",
        "Account": "계정", "Login": "로그인", "Register": "회원가입", "Admin Users": "관리자 사용자",
        "Audit Log": "감사 로그", "Change Report": "변경 리포트", "Workspace": "워크스페이스",
        "Monitoring active": "모니터링 작동 중", "Refresh": "새로고침", "Refresh Now": "지금 새로고침",
        "Manage": "관리", "Export Report": "리포트 내보내기", "Hosts Online": "온라인 호스트",
        "Hosts Offline": "오프라인 호스트", "Hosts Warning": "주의 호스트", "Hosts Critical": "위험 호스트",
        "Recent Alerts": "최근 알림", "Threshold Alerts": "임계값 알림", "Recovery Alerts": "복구 알림",
        "Anomaly Alerts": "이상 징후 알림", "No recent alerts.": "최근 알림이 없습니다.",
        "No data loaded yet.": "아직 불러온 데이터가 없습니다.", "Save": "저장", "Reload": "다시 불러오기",
        "Settings": "설정", "Search": "검색", "Filter": "필터", "Clear": "초기화", "Close": "닫기",
        "Cancel": "취소", "Delete": "삭제", "Edit": "편집", "Add": "추가", "Apply": "적용",
        "Details": "상세", "History": "이력", "Status": "상태", "Host": "호스트", "Time": "시간",
        "Date": "날짜", "User": "사용자", "Reason": "사유", "Message": "메시지", "Value": "값",
        "Online": "온라인", "Offline": "오프라인", "Warning": "주의", "Critical": "위험",
        "Loading...": "불러오는 중...", "No results found.": "결과가 없습니다.", "No records found.": "기록이 없습니다.",
        "Account Center": "계정 센터", "My profile": "내 프로필", "My Profile": "내 프로필", "Display name": "표시 이름",
        "Created": "생성일", "Last login": "최근 로그인", "Current password": "현재 비밀번호", "New password": "새 비밀번호",
        "Confirm new password": "새 비밀번호 확인", "Two-factor authentication": "2차 인증", "Setup key": "설정 키",
        "Verification code": "인증 코드", "Recovery codes": "복구 코드", "Active sessions": "활성 세션",
        "Revoked": "폐기됨", "Active": "활성", "Current": "현재", "Admin Center": "관리자 센터",
        "Audit log": "감사 로그", "Change report": "변경 리포트", "Back to user management": "사용자 관리로 돌아가기",
        "All actions": "모든 작업", "All status": "모든 상태", "Success": "성공", "Failed": "실패",
        "Actor": "수행자", "Target": "대상", "Configuration changes": "구성 변경", "Security changes": "보안 변경",
        "Restores": "복원", "Role / access changes": "역할/접근 변경", "All types": "모든 유형",
        "Config": "구성", "Security": "보안", "Access": "접근", "Restore": "복원", "Actions": "작업",
        "Alert history": "알림 이력", "Back to dashboard": "대시보드로 돌아가기", "Alerts loaded": "알림 불러옴",
        "Threshold": "임계값", "Recovery": "복구", "Anomaly": "이상 징후", "All hosts": "모든 호스트",
        "All categories": "모든 카테고리", "Alert": "알림", "Audit": "감사", "Rows per page": "페이지당 행 수",
        "Previous": "이전", "Next": "다음", "System": "시스템", "Dependency status": "의존 서비스 상태",
        "Event timeline": "이벤트 타임라인", "Slow Queries": "느린 쿼리", "Blocking": "블로킹", "Top IO": "상위 IO",
        "Performance Charts": "성능 차트", "Current focus": "현재 기준", "Range": "범위", "Real-time": "실시간",
        "Reset Zoom": "확대/축소 초기화", "Alert Rules": "알림 규칙", "Configuration": "구성", "Enabled": "사용",
        "Cooldown (min)": "재알림 대기(분)", "Maintenance Window": "점검 시간", "Start": "시작", "End": "종료",
        "View Details": "상세 보기", "Disk Used": "디스크 사용량", "Disk Total": "디스크 전체"
        ,"OpsEye | Account": "OpsEye | 계정", "OpsEye | Audit Log": "OpsEye | 감사 로그",
        "OpsEye | Change Report": "OpsEye | 변경 리포트", "OpsEye | Admin Users": "OpsEye | 관리자 사용자",
        "OpsEye | Dependencies": "OpsEye | 의존 서비스", "OpsEye | Event Timeline": "OpsEye | 이벤트 타임라인",
        "OpsEye | Alert History": "OpsEye | 알림 이력", "OpsEye | Server Compare": "OpsEye | 서버 비교",
        "OpsEye | System Status": "OpsEye | 시스템 상태", "Java Log Parser": "Java 로그 파서",
        "Java Log Analyzer": "Java 로그 분석기", "IIS Log Parser": "IIS 로그 파서", "Log Entries": "로그 항목",
        "Analyzing...": "분석 중...", "Analyze": "분석", "Analyze Batch": "일괄 분석", "Analysis Snapshot": "분석 요약",
        "Batch Export Preview": "일괄 내보내기 미리보기", "All": "전체", "Timestamp": "타임스탬프",
        "Level": "레벨", "Thread": "스레드", "Logger": "로거", "Source": "소스", "IP": "IP",
        "Country": "국가", "City": "도시", "ISP": "ISP", "AS": "AS", "Host": "호스트",
        "Group": "그룹", "Tags": "태그", "SQL Access": "SQL 접근", "Alert Override": "알림 재정의",
        "Target URL": "대상 URL", "DB Access": "DB 접근", "SQL Authentication": "SQL 인증",
        "Windows / Integrated": "Windows / 통합 인증", "Host data not found.": "호스트 데이터를 찾을 수 없습니다.",
        "Last 24h": "최근 24시간", "Last 7 days": "최근 7일", "Last 30 days": "최근 30일",
        "Last 12 months": "최근 12개월", "Request ID:": "요청 ID:", "Not Found": "찾을 수 없음",
        "Sorry, the content you are looking for does not exist.": "요청하신 콘텐츠가 존재하지 않습니다.",
        "Verify Email": "이메일 인증", "Reset Password": "비밀번호 재설정", "Two-Factor Verification": "2차 인증",
        "Development Mode": "개발 모드", "Backup archive": "백업 보관함", "File": "파일", "Size": "크기",
        "When": "시각", "Members": "회원", "Create member": "회원 생성", "Selected member": "선택한 회원",
        "User": "사용자", "Display": "표시 이름", "Role": "역할", "Actions": "작업", "Inactive": "비활성",
        "Recent login attempts": "최근 로그인 시도", "All severity": "모든 심각도", "High": "높음",
        "Medium": "중간", "Low": "낮음", "Value / Threshold": "값 / 임계값", "Metric": "지표",
        "Type": "유형", "Ratio": "비율", "Statement": "SQL 문", "Session": "세션", "Command": "명령",
        "Elapsed(ms)": "경과(ms)", "CPU(ms)": "CPU(ms)", "Reads": "읽기", "Writes": "쓰기", "Wait": "대기",
        "Block": "차단", "Top IO Queries": "상위 IO 쿼리", "TX Mbps": "TX Mbps", "RX Mbps": "RX Mbps",
        "MEM": "메모리", "OS": "운영체제", "Memory": "메모리", "Disk": "디스크", "Enabled at": "활성화 시각",
        "Failed logins": "로그인 실패 횟수", "Last failed login": "최근 로그인 실패", "Lockout until": "잠금 해제 시각"
        ,"Infrastructure": "인프라", "Server Name": "서버 이름", "Ollama": "Ollama", "Status Group": "상태 그룹",
        "Status Code": "상태 코드", "URL Contains": "URL 포함", "Method": "메서드", "IP / Text Search": "IP / 텍스트 검색",
        "Exclude IPs": "제외 IP", "Presets": "프리셋", "Render": "표시 방식", "Virtual scroll": "가상 스크롤",
        "Total Requests": "전체 요청 수", "Unique IPs": "고유 IP 수", "Detected URLs": "탐지된 URL",
        "Status Analysis": "상태 코드 분석", "Count": "건수", "Top URLs": "상위 URL", "Hits": "호출 수",
        "Filter, analyze, and export the currently visible log set.": "현재 표시된 로그를 필터링·분석·내보냅니다.",
        "Run Export Now": "지금 내보내기 실행", "Copy Block IPs": "차단 IP 복사", "Copy RateLimit IPs": "RateLimit IP 복사",
        "Multi-sort": "다중 정렬", "Rows": "행", "Export Excel": "Excel 내보내기", "Export": "내보내기",
        "Resize Date column": "날짜 열 크기 조정", "Resize Time column": "시간 열 크기 조정", "Resize IP column": "IP 열 크기 조정",
        "Resize Method column": "메서드 열 크기 조정", "Resize URI column": "URI 열 크기 조정", "Resize Status column": "상태 열 크기 조정",
        "Resize Referer column": "Referer 열 크기 조정", "Resize User-Agent column": "User-Agent 열 크기 조정",
        "First": "처음", "Prev": "이전", "Last": "마지막", "Date": "날짜", "URI": "URI", "Referer": "Referer",
        "Configuration": "구성", "Settings": "설정", "Basic": "기본", "Log Pipeline": "로그 파이프라인",
        "AI & Reports": "AI 및 리포트", "Integrations": "연동", "Server Health Alerts": "서버 상태 알림",
        "Tune when the dashboard should raise warnings for CPU, memory, disk, recovery, or maintenance windows.": "CPU·메모리·디스크·복구·점검 시간에 경고를 발생시킬 조건을 설정합니다.",
        "CPU Minutes": "CPU 지속 시간(분)", "RAM %": "RAM %", "Cooldown": "재알림 대기", "Recovery Cooldown": "복구 재알림 대기",
        "Anomaly Cooldown": "이상 징후 재알림 대기", "Window Start": "점검 시작", "Window End": "점검 종료",
        "Account Security": "계정 보안", "Turn two-factor authentication on or off for the account system.": "계정 시스템의 2차 인증을 켜거나 끕니다.",
        "IIS Log Collection": "IIS 로그 수집", "These settings decide where IIS logs are read from and when they are copied into the reporting folder.": "IIS 로그를 읽을 위치와 리포트 폴더로 복사할 시점을 설정합니다.",
        "Daily log export": "일일 로그 내보내기", "IIS log folder": "IIS 로그 폴더", "Export folder": "내보내기 폴더",
        "Export Hour": "내보내기 시", "Export Minute": "내보내기 분", "Export Offset Days": "내보내기 기준일 오프셋",
        "Daily log import": "일일 로그 가져오기", "Import log folder": "가져오기 로그 폴더", "Import state file": "가져오기 상태 파일",
        "Import Hour": "가져오기 시", "Import Minute": "가져오기 분", "Import Offset Days": "가져오기 기준일 오프셋",
        "Server name for reports": "리포트용 서버 이름", "AI Analysis": "AI 분석",
        "These settings control whether the app sends IIS log data to Ollama and how long it waits for answers.": "IIS 로그를 Ollama에 보낼지와 응답 대기 시간을 설정합니다.",
        "AI Analysis": "AI 분석", "Ollama Base URL": "Ollama 기본 URL", "Ollama Model": "Ollama 모델",
        "Ollama Timeout (sec)": "Ollama 제한 시간(초)", "Auto analyze": "자동 분석", "Auto Analyze Threshold": "자동 분석 기준",
        "Analysis output folder": "분석 출력 폴더", "This is where the latest IIS analysis JSON is saved and loaded from.": "최신 IIS 분석 JSON을 저장하고 불러올 폴더입니다.",
        "AI analysis system prompt": "AI 분석 시스템 프롬프트", "Additional question instructions": "추가 질문 지침",
        "Log Analysis Rules": "로그 분석 규칙", "Excluded IP patterns": "제외 IP 패턴", "Priority URL tokens": "우선 URL 토큰",
        "Admin probe tokens": "관리자 탐색 토큰", "Backup probe tokens": "백업 탐색 토큰", "Config probe tokens": "구성 파일 탐색 토큰",
        "Script / exploit tokens": "스크립트 / 익스플로잇 토큰", "Reports": "리포트", "Human-readable report": "사람이 읽을 수 있는 리포트",
        "No IIS analysis loaded yet.": "아직 IIS 분석을 불러오지 않았습니다.", "Load Latest IIS Report": "최신 IIS 리포트 불러오기",
        "Run Log Export Now": "지금 로그 내보내기 실행", "Download JSON": "JSON 다운로드", "Raw JSON": "원본 JSON",
        "Backup & Restore": "백업 및 복원", "Import backup file": "백업 파일 가져오기", "Backup JSON": "백업 JSON",
        "Generate Backup": "백업 생성", "Download Backup": "백업 다운로드", "Restore Backup": "백업 복원",
        "Email Verification / Reset": "이메일 인증 / 재설정", "SMTP auth mode": "SMTP 인증 모드", "Signup verification": "회원가입 이메일 인증",
        "Password reset": "비밀번호 재설정", "SMTP Host": "SMTP 호스트", "SMTP Port": "SMTP 포트", "SSL": "SSL",
        "Username": "사용자 이름", "From Address": "발신 주소", "From Name": "발신자 이름", "Verification Subject": "인증 제목",
        "Notifications": "알림", "Choose which channels receive server health alerts.": "서버 상태 알림을 받을 채널을 선택합니다.",
        "Slack Webhook URL": "Slack Webhook URL", "Teams Webhook URL": "Teams Webhook URL", "Dooray Channel ID": "Dooray 채널 ID",
        "Dooray API Token": "Dooray API 토큰", "Discord Webhook URL": "Discord Webhook URL", "Kakao Work Incoming Webhook URL": "카카오워크 Incoming Webhook URL",
        "Generic Webhook": "일반 Webhook", "Webhook URL": "Webhook URL",
        "Database": "데이터베이스", "Healthy": "정상", "Unavailable": "사용 불가", "Error": "오류",
        "Disabled": "비활성", "Configured": "설정됨", "Needs attention": "확인 필요", "Ready": "준비됨", "Missing": "누락",
        "Connected to MonitoringDb.": "MonitoringDb에 연결됨", "Cannot connect to MonitoringDb.": "MonitoringDb에 연결할 수 없습니다.",
        "No Ollama base URL configured.": "Ollama 기본 URL이 설정되지 않았습니다.", "Email/SMTP settings are incomplete.": "이메일/SMTP 설정이 완전하지 않습니다.",
        "SQL Targets": "SQL 대상", "No SQL target entries configured.": "설정된 SQL 대상이 없습니다.", "Dependency checks": "의존 서비스 점검",
        "Dependencies and backup status that need action.": "확인이 필요한 의존 서비스와 백업 상태입니다.", "Backup retention": "백업 보관 기간",
        "Old archives are pruned automatically.": "오래된 보관 파일은 자동으로 정리됩니다.", "Latest backup age": "최근 백업 경과 시간",
        "Stale backups are highlighted here.": "오래된 백업은 여기에서 강조 표시됩니다.", "Backup archive": "백업 보관함",
        "Auto backup enabled": "자동 백업 사용 중", "No backup archives yet.": "아직 백업 보관 파일이 없습니다.",
        "Log:": "로그:", "Output:": "출력:"
    };
    function translate(root, korean) {
        if (!root) return;
        for (const element of [root, ...root.querySelectorAll("*")]) {
            for (const attribute of ["placeholder", "title", "aria-label"]) {
                const original = element.getAttribute?.(attribute);
                if (!original) continue;
                const value = korean ? dictionary[original] : Object.keys(dictionary).find(k => dictionary[k] === original);
                if (value) element.setAttribute(attribute, value);
            }
        }
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        const nodes = [];
        while (walker.nextNode()) nodes.push(walker.currentNode);
        for (const node of nodes) {
            const parent = node.parentElement;
            if (!parent || ["SCRIPT", "STYLE", "TEXTAREA", "INPUT", "CODE"].includes(parent.tagName)) continue;
            const original = node.nodeValue;
            if (!original || !original.trim()) continue;
            const trimmed = original.trim();
            const value = korean ? dictionary[trimmed] : Object.keys(dictionary).find(k => dictionary[k] === trimmed);
            if (value) {
                node.nodeValue = original.replace(trimmed, value);
                continue;
            }
            const replacements = korean
                ? Object.entries(dictionary)
                : Object.entries(dictionary).map(([english, koreanText]) => [koreanText, english]);
            let replaced = original;
            for (const [from, to] of replacements.sort((a, b) => b[0].length - a[0].length)) {
                if (replaced.includes(from)) replaced = replaced.split(from).join(to);
            }
            if (replaced !== original) node.nodeValue = replaced;
        }
    }
    let observer;
    let currentKorean = false;
    window.opsLanguage = {
        apply(code) {
            const korean = code === "ko";
            currentKorean = korean;
            document.documentElement.lang = korean ? "ko" : "en";
            translate(document.body, korean);
            translate(document.head, korean);
            if (!observer) {
                observer = new MutationObserver(records => {
                    if (window.opsLanguage._busy) return;
                    window.opsLanguage._busy = true;
                    for (const record of records) for (const node of record.addedNodes)
                        if (node.nodeType === Node.ELEMENT_NODE) translate(node, currentKorean);
                    window.opsLanguage._busy = false;
                });
                observer.observe(document.documentElement, { childList: true, subtree: true });
            }
        },
        _busy: false
    };
})();
