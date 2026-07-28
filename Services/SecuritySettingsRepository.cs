using System.Text.Json;

namespace Monitoring.Blazor.Services;

public sealed class SecuritySettingsRepository
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly object _lock = new();

    public SecuritySettingsRepository(IConfiguration configuration)
    {
        _path = Path.Combine(ResolveDataDirectory(configuration), "security-settings.json");
    }

    public SecuritySettings Load(SecuritySettings fallback)
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return fallback;
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<SecuritySettings>(json, _options);
            return loaded ?? fallback;
        }
    }

    public void Save(SecuritySettings settings)
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
