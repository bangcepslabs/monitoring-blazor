using System.Text.Json;

namespace Monitoring.Blazor.Services;

public sealed class RuntimeSettingsRepository
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly object _lock = new();

    public RuntimeSettingsRepository(IConfiguration configuration)
    {
        _baseDirectory = ResolveDataDirectory(configuration);
    }

    public OllamaRuntimeSettings LoadOllama(OllamaRuntimeSettings fallback) => Load("ollama-runtime.json", fallback);

    public void SaveOllama(OllamaRuntimeSettings settings) => Save("ollama-runtime.json", settings);

    public LogExportRuntimeSettings LoadLogExport(LogExportRuntimeSettings fallback) => Load("log-export-runtime.json", fallback);

    public void SaveLogExport(LogExportRuntimeSettings settings) => Save("log-export-runtime.json", settings);

    public LogImportRuntimeSettings LoadLogImport(LogImportRuntimeSettings fallback) => Load("log-import-runtime.json", fallback);

    public void SaveLogImport(LogImportRuntimeSettings settings) => Save("log-import-runtime.json", settings);

    public LogAnalysisRuntimeSettings LoadLogAnalysis(LogAnalysisRuntimeSettings fallback) => Load("log-analysis-runtime.json", fallback);

    public void SaveLogAnalysis(LogAnalysisRuntimeSettings settings) => Save("log-analysis-runtime.json", settings);

    private T Load<T>(string fileName, T fallback)
    {
        lock (_lock)
        {
            var path = GetPath(fileName);
            if (!File.Exists(path))
            {
                return fallback;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<T>(json, _options);
            return loaded is null ? fallback : loaded;
        }
    }

    private void Save<T>(string fileName, T settings)
    {
        lock (_lock)
        {
            var path = GetPath(fileName);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, _options);
            File.WriteAllText(path, json);
        }
    }

    private string GetPath(string fileName) => Path.Combine(_baseDirectory, fileName);

    private static string ResolveDataDirectory(IConfiguration configuration)
    {
        var configured = configuration.GetValue<string>("Monitoring:DataDirectory");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return AppContext.BaseDirectory;
    }
}

public sealed class OllamaRuntimeSettings
{
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma3:4b";
    public int TimeoutSeconds { get; set; } = 1200;
    public bool AutoAnalyzeEnabled { get; set; } = true;
    public double AutoAnalyzeStatus5xxRatio { get; set; } = 0.05;
    public string AnalysisOutputFolder { get; set; } = string.Empty;
}

public sealed class LogExportRuntimeSettings
{
    public bool Enabled { get; set; } = false;
    public string LogFolder { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public int RunHour { get; set; } = 9;
    public int RunMinute { get; set; } = 0;
    public int TargetDateOffsetDays { get; set; } = 1;
}

public sealed class LogImportRuntimeSettings
{
    public bool Enabled { get; set; } = false;
    public string LogFolder { get; set; } = string.Empty;
    public int RunHour { get; set; } = 9;
    public int RunMinute { get; set; } = 5;
    public int TargetDateOffsetDays { get; set; } = 1;
    public string StateFilePath { get; set; } = string.Empty;
    public string ServerName { get; set; } = "HOST-01";
}

public sealed class LogAnalysisRuntimeSettings
{
    public string AnalysisSystemPrompt { get; set; } = LogAnalysisPromptDefaults.SystemPrompt;
    public string QuestionInstructions { get; set; } = LogAnalysisPromptDefaults.QuestionInstructions;
    public List<string> ExcludedIpPatterns { get; set; } = [];
    public List<string> PriorityUrlTokens { get; set; } = [];
    public List<string> AdminProbeTokens { get; set; } = [];
    public List<string> BackupProbeTokens { get; set; } = [];
    public List<string> ConfigProbeTokens { get; set; } = [];
    public List<string> ScriptProbeTokens { get; set; } = [];
}

public static class LogAnalysisPromptDefaults
{
    public static string SystemPrompt => string.Concat(
        "너는 IIS/ASP.NET 보안 로그를 분석하는 SOC 분석가다. ",
        "반드시 한국어로만 답변하라. ",
        "입력은 압축된 IIS 로그 분석 JSON이다. ",
        "일반 통계 설명, 브라우저/OS/User-Agent 설명은 하지 마라. ",
        "ExcludedIpPatterns에 해당하는 IP는 이미 분석에서 제외되었으므로 출력하지 마라. ",
        "반드시 다음 항목을 우선 분석하라: TopUrlAttackCandidates, ScoredIps, NotFoundScanIps, BoardAttackCandidates, SuspiciousQueryStrings, Top404Urls, Top404Ips. ",
        "HTTP 200 정상 응답이라도 동일 URL 또는 동일 IP 반복 호출은 스크래핑/DDoS성 접근 후보로 판단하라. ",
        "404가 많은 IP, 관리자/백업/설정 파일 접근 시도, 특정 URL 집중 접근을 중요하게 판단하라. ",
        "차단 후보는 RecommendedAction이 Block 또는 RateLimit인 항목만 출력하라. ",
        "IP와 URL은 JSON에 있는 값만 그대로 출력하고 추정하거나 잘라서 만들지 마라. ",
        "동일 IP와 동일 URL은 중복 출력하지 마라. ",
        "의심 IP는 반드시 IP / 점수 / 주요 근거 3개를 함께 출력하라. ",
        "공격 URL은 URL / 요청 수 / 상위 IP / 상위 IP 점유율을 출력하라. ",
        "차단 후보는 즉시 차단, 속도 제한, 모니터링으로 구분하라. ",
        "점수만 출력하지 말고 점수 산정 근거를 함께 출력하라. ",
        "각 판단에는 요청 수, 반복 URL, Referer 없음 비율, 오류 수, 의심 QueryString 중 가능한 근거를 포함하라. ",
        "절대 Okay, OK, 알겠습니다, 이해했습니다 같은 확인만 출력하지 마라. ",
        "출력 형식은 [전체 요약] [위험도] [공격 URL] [의심 IP] [차단 후보] [대응 방안] 순서로 작성하라. ",
        "각 항목은 1~3문장으로 구체적으로 쓰고, 각 섹션을 반드시 채워라. ",
        "한 문장에 수치를 나열하지 말고 핵심 수치는 최대 3개만 남겨라. ",
        "URL/IP 항목은 '대상 · 권고 · 핵심 수치' 순서로 짧게 작성하라.");

    public const string QuestionInstructions =
        "사용자 추가 질문이 있다. 기본 보고서를 반복하는 것으로 끝내지 말고, 질문에 직접 답하는 [추가 질문 답변] 섹션을 반드시 마지막에 작성하라. " +
        "추가 질문의 결론, 관련 URL/IP, 근거 수치, 권고 조치를 JSON에서 찾아 구체적으로 작성하라. " +
        "질문과 관련된 데이터가 없으면 없다고 명확히 말하되 확인문구만 출력하지 마라.";
}
