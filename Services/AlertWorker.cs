using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class AlertWorker(
    AlertDispatcher dispatcher,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<MonitoringDbContext> dbFactory,
    NotificationSettingsRepository notificationSettingsRepository,
    ILogger<AlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in dispatcher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SaveAlertAsync(message, stoppingToken);
                await SendNotificationChannelsAsync(message, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send alert.");
            }
        }
    }

    private async Task SaveAlertAsync(AlertMessage message, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.AlertEvents.Add(new AlertEventEntity
            {
                Hostname = message.Hostname,
                Ip = message.Ip,
                Os = message.Os,
                Metric = message.Metric,
                Value = message.Value,
                Threshold = message.Threshold,
                Message = message.Message,
                Type = message.Type,
                TimestampUtc = message.TimestampUtc
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist alert event.");
        }
    }

    private async Task SendDoorayAsync(AlertMessage message, NotificationSettings settings, CancellationToken ct)
    {
        var enabled = settings.DoorayEnabled || configuration.GetValue("Monitoring:Dooray:Enabled", false);
        var channelId = string.IsNullOrWhiteSpace(settings.DoorayChannelId)
            ? configuration["Monitoring:Dooray:ChannelId"]
            : settings.DoorayChannelId;
        var apiToken = string.IsNullOrWhiteSpace(settings.DoorayApiToken)
            ? configuration["Monitoring:Dooray:ApiToken"]
            : settings.DoorayApiToken;

        if (!enabled || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(apiToken))
        {
            logger.LogInformation("Dooray alert skipped (not configured).");
            return;
        }

        var url = $"https://api.gov-dooray.com/messenger/v1/channels/{channelId}/logs";
        var title = message.Type switch
        {
            AlertType.Recovery => "[Monitoring Recovery]",
            AlertType.Anomaly => "[Monitoring Anomaly]",
            _ => "[Monitoring Alert]"
        };

        var text = $"{title}\n" +
                   $"Host: {message.Hostname}\n" +
                   $"IP: {message.Ip}\n" +
                   $"OS: {message.Os}\n" +
                   $"Metric: {message.Metric}\n" +
                   $"Value: {message.Value:0.0}%\n" +
                   $"Threshold: {message.Threshold:0.0}%\n" +
                   $"Time(UTC): {message.TimestampUtc:yyyy-MM-dd HH:mm:ss}\n" +
                   $"AlertReason: {message.Message}";

        var payload = JsonSerializer.Serialize(new { text });

        var client = httpClientFactory.CreateClient(nameof(AlertWorker));
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"dooray-api {apiToken}");
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendNotificationChannelsAsync(AlertMessage message, CancellationToken ct)
    {
        var fallback = configuration.GetSection("Monitoring:Notifications").Get<NotificationSettings>() ?? new NotificationSettings();
        var settings = notificationSettingsRepository.Load(fallback);
        await SendDoorayAsync(message, settings, ct);
        if (!settings.Enabled)
        {
            logger.LogInformation("Notification channels skipped (disabled).");
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            text = BuildNotificationText(message)
        });

        var tasks = new List<Task>();
        if (settings.SlackEnabled && !string.IsNullOrWhiteSpace(settings.SlackWebhookUrl))
        {
            tasks.Add(SendWebhookAsync("Slack", settings.SlackWebhookUrl, payload, ct));
        }

        if (settings.TeamsEnabled && !string.IsNullOrWhiteSpace(settings.TeamsWebhookUrl))
        {
            tasks.Add(SendWebhookAsync("Teams", settings.TeamsWebhookUrl, payload, ct));
        }

        if (settings.DiscordEnabled && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
        {
            var discordPayload = JsonSerializer.Serialize(new { content = BuildNotificationText(message) });
            tasks.Add(SendWebhookAsync("Discord", settings.DiscordWebhookUrl, discordPayload, ct));
        }

        if (settings.KakaoWorkEnabled && !string.IsNullOrWhiteSpace(settings.KakaoWorkWebhookUrl))
        {
            tasks.Add(SendWebhookAsync("KakaoWork", settings.KakaoWorkWebhookUrl, payload, ct));
        }

        if (settings.WebhookEnabled && !string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            tasks.Add(SendWebhookAsync("Webhook", settings.WebhookUrl, payload, ct));
        }

        if (tasks.Count == 0)
        {
            logger.LogInformation("Notification channels skipped (no endpoints configured).");
            return;
        }

        await Task.WhenAll(tasks);
    }

    private async Task SendWebhookAsync(string channelName, string url, string payload, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(nameof(AlertWorker));
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("{Channel} alert sent.", channelName);
    }

    private static string BuildNotificationText(AlertMessage message)
    {
        return $"[{message.Type}] {message.Hostname}\n" +
               $"IP: {message.Ip}\n" +
               $"OS: {message.Os}\n" +
               $"Metric: {message.Metric}\n" +
               $"Value: {message.Value:0.0}%\n" +
               $"Threshold: {message.Threshold:0.0}%\n" +
               $"Time(UTC): {message.TimestampUtc:yyyy-MM-dd HH:mm:ss}\n" +
               $"Message: {message.Message}";
    }
}
