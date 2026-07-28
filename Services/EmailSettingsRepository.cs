using System.Text.Json;

namespace Monitoring.Blazor.Services;

public sealed class EmailSettingsRepository
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly object _lock = new();

    public EmailSettingsRepository(IConfiguration configuration)
    {
        _path = Path.Combine(ResolveDataDirectory(configuration), "email-settings.json");
    }

    public EmailSettings Load(EmailSettings fallback)
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return fallback;
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<EmailSettings>(json, _options);
            return loaded ?? fallback;
        }
    }

    public void Save(EmailSettings settings)
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
