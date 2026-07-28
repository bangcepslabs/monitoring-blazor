using System.Text.Json;

namespace Monitoring.Blazor.Services;

public sealed class ServerCatalogRepository
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly object _lock = new();

    public ServerCatalogRepository(IConfiguration configuration)
    {
        _path = Path.Combine(ResolveDataDirectory(configuration), "server-catalog.json");
    }

    public Dictionary<string, ServerMetadata> LoadAll()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return new Dictionary<string, ServerMetadata>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ServerMetadata>>(json, _options);
            return loaded is not null
                ? new Dictionary<string, ServerMetadata>(loaded, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ServerMetadata>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public ServerMetadata Get(string hostname)
    {
        var all = LoadAll();
        return all.TryGetValue(hostname, out var metadata) && metadata is not null
            ? metadata
            : new ServerMetadata();
    }

    public void Save(string hostname, ServerMetadata metadata)
    {
        lock (_lock)
        {
            var all = LoadAll();
            all[hostname] = metadata;
            WriteAll(all);
        }
    }

    public void Remove(string hostname)
    {
        lock (_lock)
        {
            var all = LoadAll();
            if (!all.Remove(hostname))
            {
                return;
            }

            WriteAll(all);
        }
    }

    private void WriteAll(Dictionary<string, ServerMetadata> metadata)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(metadata, _options);
        File.WriteAllText(_path, json);
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

