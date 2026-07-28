# OpsEye

## 1) 프로젝트 구조 요약

### 주요 폴더
- `Components/Pages/`: 화면 페이지(라우팅 단위)
- `Components/Layout/`: 레이아웃/사이드바/상단바 등 공통 UI
- `Services/`: 백엔드 로직, 워커, 알림, 로그 파서 등
- `Models/`: 데이터 모델
- `wwwroot/`: 정적 파일(CSS/JS/이미지)
- `Program.cs`: DI 등록, API 엔드포인트, 미들웨어 설정

### 핵심 흐름
- **클라이언트 → 서버** 상태 수신: `/api/monitor/client-message`
- **대시보드 표시**: `Components/Pages/Home.razor`
- **로그 분석 페이지**: `Components/Pages/LogParser.razor`
- **알림 처리**: `Services/AlertEvaluator.cs` → `AlertDispatcher` → `AlertWorker`
- **자동 로그 내보내기**: `Services/LogAutoExportWorker.cs` + `LogAutoExportService.cs`


## 2) 페이지별 수정 방법

### A. 대시보드 (Home)
파일: `Components/Pages/Home.razor`

- 카드/차트/테이블 등 UI 수정은 여기서
- 차트 데이터 푸시는 `PushChartDataAsync`에서 처리
- 알림 규칙 UI는 `AlertSettings` 바인딩 부분 확인
- 자동 새로고침은 `AutoRefresh`와 `monitorSettings`(localStorage) 사용


### B. 로그 분석 (LogParser)
파일: `Components/Pages/LogParser.razor`

- 업로드 UI/필터/검색/페이징/가상스크롤 제어
- 필터 로직: `GetFilteredRows()`
- IP 제외 필터: `ExcludeIpQuery`
- 즉시 내보내기 버튼: `Run Export Now`
- 진행률 표시: 업로드 시 `IsUploading`/`UploadProgress`


### C. 레이아웃/메뉴/상단바
파일:
- `Components/Layout/MainLayout.razor`
- `Components/Layout/MainLayout.razor.css`
- `Components/Layout/NavMenu.razor`
- `Components/Layout/NavMenu.razor.css`

수정 포인트:
- 상단바 버튼/색상/배치는 `MainLayout.razor.css`
- 사이드 메뉴는 `NavMenu.razor(.css)`


## 3) CSS 구조

- 전역 변수/공통 스타일: `wwwroot/app.css`
- 레이아웃 스코프 CSS: `MainLayout.razor.css`
- 사이드바 스코프 CSS: `NavMenu.razor.css`

Blazor CSS Isolation 때문에 `obj/**/scopedcss`에 빌드 산출물 생성됨. 이 파일은 수정하지 않음.


## 4) 서비스/백엔드 기능

### A. 알림
- 설정 저장: `AlertSettingsRepository.cs` (파일 저장: `alert-settings.json`)
- 평가: `AlertEvaluator.cs`
- 발송: `AlertWorker.cs` → Dooray API

### B. 로그 파서
- IIS 로그 파싱: `Services/LogParser.cs` (`ApacheLogParser.ParseLines`)
- 로그 결과 저장: `Program.cs`에서 `/api/monitor/parse-logs` 처리

### C. 자동 로그 내보내기
- 워커: `LogAutoExportWorker.cs` (매일 09:00 실행)
- 즉시 실행: `LogAutoExportService.cs` + API `POST /api/log-export/run`
- 출력 폴더/시간 설정: `appsettings.json`의 `Monitoring:LogExport`

### D. 보관 기간 정책
- 워커: `DataRetentionWorker.cs`
- 기본값:
  - `HostSnapshots`: 30일
  - `AlertEvents`: 90일
  - `LogIpDailyStats`: 365일
- 설정 위치: `Monitoring:Retention`

### E. IIS 로그 매일 자동 수집
- 현재 수동 업로드는 `LogParser.razor`에서 처리
- 자동 수집 워커: `LogAutoImportWorker.cs` + `LogAutoImportService.cs`
- 같은 패턴으로 `BackgroundService`를 하나 더 만들어서
  - `C:\inetpub\logs\LogFiles\W3SVC3` 같은 폴더를 매일 오전에 스캔
  - 새 파일만 가져와 DB에 적재
  - 완료 후 집계 테이블 갱신
- 설정 위치: `Monitoring:LogImport`
- 구현 난이도는 높지 않지만, 중복 수집 방지를 위해
  - 마지막 처리 파일 기록
  - 파일 해시 또는 수정 시간 비교
  - 에러 재시도 정책
  이 3가지는 같이 넣는 게 좋습니다


## 5) 자주 수정하는 설정

### appsettings.json
- Dooray 알림: `Monitoring:Dooray`
- 알림 규칙 기본값: `Monitoring:Alerts`
- 로그 자동 내보내기: `Monitoring:LogExport`
- DB 연결: `ConnectionStrings:MonitoringDb`

### appsettings.local.json
- 로컬 전용 비밀번호/환경 변수 덮어쓰기


## 6) 데이터 흐름 간단 요약

1) 클라이언트가 `/api/monitor/client-message`로 상태 전송
2) 서버가 상태 저장 (`MonitorStateService`) + 알림 평가
3) UI(Home.razor)가 상태를 표시
4) 알림이 조건 충족 시 Dooray 발송


## 7) 처음 온 개발자를 위한 체크리스트

- 빌드: `dotnet build`
- 실행: `dotnet run --urls http://localhost:8050`
- DB 연결 문자열 확인 (SQL Server)
- `alert-settings.json` 확인
- `Monitoring:LogExport` 폴더 경로 확인


## 8) 흔한 문제/해결

- CSS가 적용 안됨: `bin/obj` 삭제 후 재빌드
- Dooray 알림 안 감: `Monitoring:Alerts.Enabled`, `Monitoring:Dooray.Enabled` 확인
- 로그 내보내기 안 됨: 경로 권한, 폴더 존재 여부 확인
- DB 로그가 너무 빨리 쌓이면: `Monitoring:Retention` 값을 줄이기


---

## 9) Blazor/C# 학습 가이드 (초보자용)

이 프로젝트를 유지보수하려면 최소한 아래 개념을 이해하면 돼요.  
처음 보는 분을 기준으로 가장 필요한 것만 정리했습니다.

### 핵심 키워드
- **Razor 컴포넌트**: `.razor` 파일. HTML + C# 코드가 섞여 있음
- **코드 블록**: `@code { ... }` 안에서 C# 로직 작성
- **바인딩(@bind)**: 입력 값 ↔ 변수 자동 연결
- **이벤트(@onclick)**: 버튼 클릭 시 C# 메서드 호출
- **DI(의존성 주입)**: `@inject` 또는 `builder.Services.Add...`로 서비스 연결
- **HostedService**: 서버에서 백그라운드로 계속 돌아가는 작업

### 예시로 이해하기

#### 1) 버튼 클릭 이벤트
```razor
<button @onclick="SaveAlertSettings">Save</button>

@code {
    private async Task SaveAlertSettings()
    {
        // 저장 로직
    }
}
```

#### 2) 입력 바인딩
```razor
<input class="form-control" @bind="SearchText" />

@code {
    private string SearchText { get; set; } = string.Empty;
}
```

#### 3) 서비스 주입
```razor
@inject NavigationManager NavigationManager

@code {
    // NavigationManager로 현재 URL 정보 사용
}
```

### 필요한 최소 문법
- `if` / `foreach` 조건 렌더링
- `List<T>` / `Dictionary<TKey,TValue>` 기본 사용
- `async/await` 비동기 호출

### 추천 학습 순서
1. Razor 기본 문법 (HTML + C# 섞는 방식)
2. @bind / @onclick 이벤트
3. DI(서비스 주입)
4. API 호출(HttpClient)
5. BackgroundService

### 유지보수 포인트 요약
- 화면: `Components/Pages/*.razor`
- 스타일: `wwwroot/app.css`, `Components/Layout/*.razor.css`
- 백엔드 로직: `Services/*.cs`
- 설정: `appsettings.json`

---

필요하면 이 문서를 `README.md`로 통합하거나, 더 짧은 운영 매뉴얼 버전으로 나눠서 제공할 수 있습니다.



