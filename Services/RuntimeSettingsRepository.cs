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
