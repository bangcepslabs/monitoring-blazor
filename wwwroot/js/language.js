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
        "Performance Charts": "성능 차트", "Current focus": "현재 초점", "Range": "범위", "Real-time": "실시간",
        "Reset Zoom": "확대/축소 초기화", "Alert Rules": "알림 규칙", "Configuration": "구성", "Enabled": "사용",
        "Cooldown (min)": "재알림 대기(분)", "Maintenance Window": "점검 시간", "Start": "시작", "End": "종료",
        "View Details": "상세 보기", "Disk Used": "디스크 사용량", "Disk Total": "디스크 전체"
    };
    function translate(root, korean) {
        if (!root) return;
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
            if (value) node.nodeValue = original.replace(trimmed, value);
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
            if (!observer) {
                observer = new MutationObserver(records => {
                    if (window.opsLanguage._busy) return;
                    window.opsLanguage._busy = true;
                    for (const record of records) for (const node of record.addedNodes)
                        if (node.nodeType === Node.ELEMENT_NODE) translate(node, currentKorean);
                    window.opsLanguage._busy = false;
                });
                observer.observe(document.body, { childList: true, subtree: true });
            }
        },
        _busy: false
    };
})();
