using System.Text.Json;

namespace Monitoring.Blazor.Services;

public sealed class NotificationSettingsRepository
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly object _lock = new();

    public NotificationSettingsRepository(IConfiguration configuration)
    {
        _path = Path.Combine(ResolveDataDirectory(configuration), "notification-settings.json");
    }

    public NotificationSettings Load(NotificationSettings fallback)
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return fallback;
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<NotificationSettings>(json, _options);
            return loaded ?? fallback;
        }
    }

    public void Save(NotificationSettings settings)
    {
        lock (_lock)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, _options);
            File.WriteAllText(_path, json);
        }
    }

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

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = false;
    public bool SlackEnabled { get; set; } = false;
    public string SlackWebhookUrl { get; set; } = string.Empty;
    public bool TeamsEnabled { get; set; } = false;
    public string TeamsWebhookUrl { get; set; } = string.Empty;
    public bool WebhookEnabled { get; set; } = false;
    public string WebhookUrl { get; set; } = string.Empty;
}
