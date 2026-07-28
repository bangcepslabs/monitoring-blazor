using System.Text.Json;

namespace Monitoring.Blazor.Services;

public sealed class OllamaAnalysisStore(
    IConfiguration configuration,
    RuntimeSettingsRepository runtimeSettingsRepository,
    ILogger<OllamaAnalysisStore> logger)
{
    private const string LatestFileName = "iis-analysis-latest.json";

    public async Task SaveAsync(OllamaAnalysisEntry entry, CancellationToken ct)
    {
        try
        {
            var folder = ResolveFolder();
            Directory.CreateDirectory(folder);

            var timestamp = entry.TimestampUtc == default ? DateTime.UtcNow : entry.TimestampUtc;
            var datedFile = $"iis-analysis-{timestamp:yyyyMMdd-HHmmss}.json";
            var latestPath = Path.Combine(folder, LatestFileName);
            var datedPath = Path.Combine(folder, datedFile);

            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(datedPath, json, ct);
            await File.WriteAllTextAsync(latestPath, json, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save Ollama analysis result.");
        }
    }

    public OllamaAnalysisEntry? LoadLatest()
    {
        try
        {
            var folder = ResolveFolder();
            var latestPath = Path.Combine(folder, LatestFileName);
            if (File.Exists(latestPath))
            {
                var json = File.ReadAllText(latestPath);
                var entry = JsonSerializer.Deserialize<OllamaAnalysisEntry>(json);
                if (entry is not null && !IsIgnorableResponse(entry.Response))
                {
                    return entry;
                }
            }

            if (!Directory.Exists(folder))
            {
                return null;
            }

            var latestFile = new DirectoryInfo(folder)
                .EnumerateFiles("iis-analysis-*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .FirstOrDefault(file =>
                {
                    try
                    {
                        var content = File.ReadAllText(file.FullName);
                        var entry = JsonSerializer.Deserialize<OllamaAnalysisEntry>(content);
                        return entry is not null && !IsIgnorableResponse(entry.Response);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (latestFile is null)
            {
                return null;
            }

            var fallbackJson = File.ReadAllText(latestFile.FullName);
            return JsonSerializer.Deserialize<OllamaAnalysisEntry>(fallbackJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Ollama analysis result.");
            return null;
        }
    }

    private static bool IsIgnorableResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return true;
        }

        var trimmed = response.Trim();
        if (trimmed.Length <= 20)
        {
            return trimmed.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("okay", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("알겠습니다", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("이해했습니다", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("좋습니다", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("Ollama is disabled in configuration.", StringComparison.OrdinalIgnoreCase);
        }

        return trimmed.Equals("Ollama is disabled in configuration.", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveFolder()
    {
        var fallback = configuration.GetSection("Monitoring:Ollama").Get<OllamaRuntimeSettings>() ?? new OllamaRuntimeSettings();
        var settings = runtimeSettingsRepository.LoadOllama(fallback);
        var analysisFolder = settings.AnalysisOutputFolder;
        if (!string.IsNullOrWhiteSpace(analysisFolder))
        {
            return analysisFolder;
        }

        var exportFolder = configuration.GetValue<string>("Monitoring:LogExport:OutputFolder");
        if (!string.IsNullOrWhiteSpace(exportFolder))
        {
            return exportFolder;
        }

        return AppContext.BaseDirectory;
    }
}

public sealed record OllamaAnalysisEntry
{
    public DateTime TimestampUtc { get; init; }
    public string Source { get; init; } = "auto";
    public DateOnly? LogDate { get; init; }
    public long TotalRows { get; init; }
    public long Status5xxCount { get; init; }
    public double Status5xxRatio { get; init; }
    public List<CountItem>? TopStatusCodes { get; init; }
    public List<CountItem>? Top5xxUris { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public string Response { get; init; } = string.Empty;
}

public sealed record CountItem(string Key, long Count);
