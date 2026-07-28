using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Monitoring.Blazor.Services;

public sealed class OllamaClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    RuntimeSettingsRepository runtimeSettingsRepository)
{
    public async Task<string> GenerateAsync(string prompt, string? systemPrompt, CancellationToken ct)
    {
        var settings = LoadSettings();
        var enabled = settings.Enabled;
        if (!enabled)
        {
            return "Ollama is disabled in configuration.";
        }

        var model = settings.Model;
        var baseUrl = settings.BaseUrl;
        var client = httpClientFactory.CreateClient(nameof(OllamaClient));
        client.BaseAddress = new Uri(baseUrl);
        var timeoutSeconds = settings.TimeoutSeconds;
        if (timeoutSeconds > 0)
        {
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        var payload = new OllamaGenerateRequest
        {
            Model = model,
            Prompt = prompt,
            System = systemPrompt,
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = 0.1,
                TopP = 0.2,
                NumPredict = 900,
                RepeatPenalty = 1.1
            }
        };

        using var response = await client.PostAsJsonAsync("/api/generate", payload, ct);
        await EnsureSuccessWithDetailsAsync(response, model, ct);
        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct);
        return result?.Response ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        string? systemPrompt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var settings = LoadSettings();
        var enabled = settings.Enabled;
        if (!enabled)
        {
            yield break;
        }

        var model = settings.Model;
        var baseUrl = settings.BaseUrl;
        var client = httpClientFactory.CreateClient(nameof(OllamaClient));
        client.BaseAddress = new Uri(baseUrl);
        var timeoutSeconds = settings.TimeoutSeconds;
        if (timeoutSeconds > 0)
        {
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        var payload = new OllamaGenerateRequest
        {
            Model = model,
            Prompt = prompt,
            System = systemPrompt,
            Stream = true,
            Options = new OllamaOptions
            {
                Temperature = 0.1,
                TopP = 0.2,
                NumPredict = 900,
                RepeatPenalty = 1.1
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessWithDetailsAsync(response, model, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string? chunk = null;
            var done = false;

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("response", out var resp))
                {
                    chunk = resp.GetString();
                }

                if (doc.RootElement.TryGetProperty("done", out var doneProp))
                {
                    done = doneProp.GetBoolean();
                }
            }
            catch (JsonException)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(chunk))
            {
                yield return chunk;
            }

            if (done)
            {
                yield break;
            }
        }
    }

    private static async Task EnsureSuccessWithDetailsAsync(HttpResponseMessage response, string model, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseText = await response.Content.ReadAsStringAsync(ct);
        var detail = TryExtractErrorMessage(responseText);
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = $"Ollama request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        if (detail.Contains("not found", StringComparison.OrdinalIgnoreCase) &&
            detail.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            detail = $"Configured Ollama model '{model}' is not installed. {detail}";
        }

        throw new InvalidOperationException(detail);
    }

    private static string? TryExtractErrorMessage(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                return errorElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Fall back to raw text below.
        }

        return responseText.Trim();
    }

    private sealed class OllamaGenerateRequest
    {
        public string Model { get; init; } = string.Empty;
        public string Prompt { get; init; } = string.Empty;
        public string? System { get; init; }
        public bool Stream { get; init; }
        public OllamaOptions? Options { get; init; }
    }

    private sealed class OllamaOptions
    {
        public double Temperature { get; init; }
        public double TopP { get; init; }
        public int NumPredict { get; init; }
        public double RepeatPenalty { get; init; }
    }

    private sealed class OllamaGenerateResponse
    {
        public string? Response { get; init; }
        public bool Done { get; init; }
    }

    private OllamaRuntimeSettings LoadSettings()
    {
        var fallback = configuration.GetSection("Monitoring:Ollama").Get<OllamaRuntimeSettings>() ?? new OllamaRuntimeSettings();
        return runtimeSettingsRepository.LoadOllama(fallback);
    }
}
