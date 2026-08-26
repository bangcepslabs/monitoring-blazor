using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;

namespace Monitoring.Blazor.Services;

public sealed class DependencyStatusService(
    IDbContextFactory<MonitoringDbContext> dbFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    NotificationSettingsRepository notificationSettingsRepository)
{
    public async Task<List<DependencyStatusEntry>> GetStatusesAsync(CancellationToken ct = default)
    {
        var items = new List<DependencyStatusEntry>
        {
            await CheckDatabaseAsync(ct),
            await CheckOllamaAsync(ct),
            CheckEmailSettings(),
            CheckNotificationSettings(),
            CheckPaths(),
            CheckSqlTargets()
        };

        return items;
    }

    private async Task<DependencyStatusEntry> CheckDatabaseAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var canConnect = await db.Database.CanConnectAsync(ct);
            return new DependencyStatusEntry("Database", canConnect ? "Healthy" : "Unavailable", canConnect ? "Connected to MonitoringDb." : "Cannot connect to MonitoringDb.", canConnect);
        }
        catch (Exception ex)
        {
            return new DependencyStatusEntry("Database", "Error", ex.Message, false);
        }
    }

    private async Task<DependencyStatusEntry> CheckOllamaAsync(CancellationToken ct)
    {
        try
        {
            var baseUrl = configuration.GetValue<string>("Monitoring:Ollama:BaseUrl");
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new DependencyStatusEntry("Ollama", "Disabled", "No Ollama base URL configured.", false);
            }

            var client = httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
            using var response = await client.GetAsync("api/tags", ct);
            return new DependencyStatusEntry("Ollama", response.IsSuccessStatusCode ? "Healthy" : "Unavailable", $"HTTP {(int)response.StatusCode}", response.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            return new DependencyStatusEntry("Ollama", "Error", ex.Message, false);
        }
    }

    private DependencyStatusEntry CheckEmailSettings()
    {
        var emailSettings = configuration.GetSection("Monitoring:Email").Get<EmailSettings>() ?? new EmailSettings();
        var configured = emailSettings.Enabled && !string.IsNullOrWhiteSpace(emailSettings.SmtpHost) && !string.IsNullOrWhiteSpace(emailSettings.FromAddress);
        return new DependencyStatusEntry("SMTP", configured ? "Configured" : "Needs attention", configured ? $"{emailSettings.SmtpHost}:{emailSettings.SmtpPort}" : "Email/SMTP settings are incomplete.", configured);
    }

    private DependencyStatusEntry CheckNotificationSettings()
    {
        var fallback = configuration.GetSection("Monitoring:Notifications").Get<NotificationSettings>() ?? new NotificationSettings();
        var settings = notificationSettingsRepository.Load(fallback);
        var configured = settings.Enabled &&
                         ((settings.SlackEnabled && !string.IsNullOrWhiteSpace(settings.SlackWebhookUrl)) ||
                          (settings.TeamsEnabled && !string.IsNullOrWhiteSpace(settings.TeamsWebhookUrl)) ||
                          (settings.DoorayEnabled && !string.IsNullOrWhiteSpace(settings.DoorayChannelId) && !string.IsNullOrWhiteSpace(settings.DoorayApiToken)) ||
                          (settings.DiscordEnabled && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl)) ||
                          (settings.KakaoWorkEnabled && !string.IsNullOrWhiteSpace(settings.KakaoWorkWebhookUrl)) ||
                          (settings.WebhookEnabled && !string.IsNullOrWhiteSpace(settings.WebhookUrl)));

        var activeChannels = new List<string>();
        if (settings.SlackEnabled && !string.IsNullOrWhiteSpace(settings.SlackWebhookUrl))
        {
            activeChannels.Add("Slack");
        }

        if (settings.TeamsEnabled && !string.IsNullOrWhiteSpace(settings.TeamsWebhookUrl))
        {
            activeChannels.Add("Teams");
        }

        if (settings.DoorayEnabled && !string.IsNullOrWhiteSpace(settings.DoorayChannelId) && !string.IsNullOrWhiteSpace(settings.DoorayApiToken))
        {
            activeChannels.Add("Dooray");
        }

        if (settings.DiscordEnabled && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
        {
            activeChannels.Add("Discord");
        }

        if (settings.KakaoWorkEnabled && !string.IsNullOrWhiteSpace(settings.KakaoWorkWebhookUrl))
        {
            activeChannels.Add("Kakao Work");
        }

        if (settings.WebhookEnabled && !string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            activeChannels.Add("Webhook");
        }

        return new DependencyStatusEntry(
            "Notifications",
            configured ? "Configured" : "Needs attention",
            configured ? string.Join(", ", activeChannels) : "Enable at least one notification channel.",
            configured);
    }

    private DependencyStatusEntry CheckPaths()
    {
        var logFolder = configuration.GetValue<string>("Monitoring:LogExport:LogFolder") ?? string.Empty;
        var outputFolder = configuration.GetValue<string>("Monitoring:LogExport:OutputFolder") ?? string.Empty;
        var ok = Directory.Exists(logFolder) && Directory.Exists(outputFolder);
        return new DependencyStatusEntry("Paths", ok ? "Ready" : "Missing", $"Log: {logFolder} | Output: {outputFolder}", ok);
    }

    private DependencyStatusEntry CheckSqlTargets()
    {
        var targets = configuration.GetSection("Monitoring:SqlTargets").GetChildren().Count();
        return new DependencyStatusEntry("SQL Targets", targets > 0 ? "Configured" : "Missing", targets > 0 ? $"{targets} target(s) configured." : "No SQL target entries configured.", targets > 0);
    }
}

public sealed record DependencyStatusEntry(string Name, string Status, string Details, bool Healthy);
