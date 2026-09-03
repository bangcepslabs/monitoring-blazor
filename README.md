# OpsEye

Blazor Server 기반 운영 모니터링 대시보드입니다. 호스트 스냅샷/알림/로그 분석을 포함합니다.

## Requirements
- .NET SDK 10.0+
- SQL Server (예: SQLEXPRESS)

## Quick Start
```powershell
dotnet restore
dotnet build
dotnet run
dotnet watch run --project .\Monitoring.Blazor.csproj --launch-profile http
```

기본 접속: `http://localhost:8050` (launchSettings 기준)

## Tests

```powershell
dotnet test .\Monitoring.Blazor.Tests\Monitoring.Blazor.Tests.csproj
```

Health endpoints:
- `GET /health/live` checks that the process is running.
- `GET /health/ready` checks database connectivity and current monitoring state.

## Local Run
- `appsettings.private.json`에 로컬 DB 값을 넣으면 됩니다.
- SQL Server가 떠 있어야 합니다.
- `Monitoring:Ingest:ApiKey`에 Agent Key를 설정해야 클라이언트 수집 요청을 받을 수 있습니다.
- 로컬에서는 `Monitoring:Ollama`, `Monitoring:LogExport`, `Monitoring:LogImport`, `Monitoring:Email`, `Monitoring:Dooray`, `Monitoring:Alerts`를 끄는 것을 권장합니다.

## Configuration
설정 파일은 `appsettings.json`/`appsettings.Development.json`에 있습니다. 비밀값은 추적되지 않는 `appsettings.private.json` 또는 환경 변수로 주입하세요.

필수 설정:
- `ConnectionStrings:MonitoringDb`

예시:
```json
{
  "ConnectionStrings": {
    "MonitoringDb": "Server=localhost\\SQLEXPRESS;Database=MonitoringDb;User Id=appUser;Password=REPLACE_ME;TrustServerCertificate=True"
  }
}
```

개인별 실제 값은 `appsettings.private.json`에 넣는 방식을 더 권장합니다.  
이 파일은 Git에 올라가지 않으며, `appsettings.private.example.json`을 복사해서 사용하면 됩니다.

## Database
앱 시작 시 스키마가 없으면 `EnsureCreated()`로 자동 생성됩니다.

생성되는 테이블:
- `HostSnapshots` (스냅샷)
- `AlertEvents` (알림 이력)
- `LogIpDailyStats` (일별 IP 집계)

## API (요약)
- `POST /api/monitor/client-message` : 클라이언트 상태 수신
- `GET /api/monitor/history?hostname=HOST&minutes=60` : 호스트 스냅샷 히스토리
- `GET /api/alerts/history?hostname=HOST&take=200` : 알림 히스토리
- `POST /api/monitor/parse-logs` : IIS 로그 업로드 및 분석/저장

## Publish (Windows)
```powershell
# Self-contained 없이 프레임워크 종속 배포

dotnet publish -c Release -o .\publish
```

IIS 배포 시:
- ASP.NET Core Hosting Bundle 설치 필요
- IIS에서 사이트 추가 후 `publish` 폴더를 경로로 지정

운영 환경에서는 `dotnet watch` 대신 `dotnet publish`로 배포합니다.

## Docker
다른 PC에서도 가장 쉽게 올리는 방법입니다.

```powershell
Copy-Item .env.example .env
# .env에서 MSSQL_SA_PASSWORD와 MONITOR_AGENT_API_KEY를 반드시 변경
docker compose up --build
```

기본 접속:
- App: `http://localhost:8080`
- SQL Server: `localhost,1433`

`.env`에서 `MSSQL_SA_PASSWORD`와 `MONITOR_AGENT_API_KEY`를 먼저 채우면 최소 구동이 가능합니다.
필요하면 `OLLAMA_*`, `SMTP_*`, `EMAIL_*`만 추가로 켭니다.
앱 설정은 `docker-data/app`에 저장되어 컨테이너를 다시 올려도 유지됩니다.

루트 저장소에서 받는 경우에는 submodule도 함께 초기화합니다.

```powershell
git clone --recurse-submodules https://github.com/jeongbyeongho/dashboard.git
cd dashboard\monitoring-blazor
```

## Operations checklist
- 운영 DB 비밀번호와 외부 Webhook/API 토큰은 저장소에 커밋하지 않습니다.
- 운영에서는 `dotnet watch`가 아닌 Release publish 결과물을 사용합니다.
- 배포 전 `Monitoring:UseHttpsRedirection`, 인증 쿠키, 백업 경로와 로그 저장 경로를 확인합니다.
- DB 연결 실패 시 `appsettings.private.json` 또는 환경 변수의 SQL 정보를 확인하세요.

## License
This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.



