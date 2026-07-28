using System.Text.Json;

namespace Monitoring.Blazor.Services;

public sealed class AlertSettingsRepository
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly object _lock = new();

    public AlertSettingsRepository(IHostEnvironment env)
    {
        _path = Path.Combine(env.ContentRootPath, "alert-settings.json");
    }

    public AlertSettings Load(AlertSettings fallback)
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return fallback;
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<AlertSettings>(json, _options);
            return loaded ?? fallback;
        }
    }

    public void Save(AlertSettings settings)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(settings, _options);
            File.WriteAllText(_path, json);
        }
    }
}
