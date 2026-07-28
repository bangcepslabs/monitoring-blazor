using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Monitoring.Blazor.Services;

public sealed class SqlTargetSettingsRepository
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly object _lock = new();

    public SqlTargetSettingsRepository(IConfiguration configuration)
    {
        _path = Path.Combine(ResolveDataDirectory(configuration), "sql-target-settings.json");
    }

    public Dictionary<string, SqlTargetSettings> LoadAll()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return new Dictionary<string, SqlTargetSettings>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, SqlTargetSettings>>(json, _options);
            return loaded is not null
                ? new Dictionary<string, SqlTargetSettings>(loaded, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, SqlTargetSettings>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public SqlTargetSettings Get(string hostname, SqlTargetSettings fallback)
    {
        var all = LoadAll();
        if (all.TryGetValue(hostname, out var settings) && settings is not null)
        {
            return settings;
        }

        return fallback;
    }

    public void Save(string hostname, SqlTargetSettings settings)
    {
        lock (_lock)
        {
            var all = LoadAll();
            all[hostname] = settings;
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

    public void ReplaceAll(Dictionary<string, SqlTargetSettings> settings)
    {
        lock (_lock)
        {
            WriteAll(new Dictionary<string, SqlTargetSettings>(settings, StringComparer.OrdinalIgnoreCase));
        }
    }

    private void WriteAll(Dictionary<string, SqlTargetSettings> settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, _options);
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

public sealed class SqlTargetSettings
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 1433;
    public string Database { get; set; } = "master";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IntegratedSecurity { get; set; }
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = true;

    public string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Server))
        {
            return string.Empty;
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Port > 0 ? $"{Server},{Port}" : Server,
            InitialCatalog = string.IsNullOrWhiteSpace(Database) ? "master" : Database,
            IntegratedSecurity = IntegratedSecurity,
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
            PersistSecurityInfo = false
        };

        if (!IntegratedSecurity)
        {
            builder.UserID = Username;
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }

    public static SqlTargetSettings FromConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var (server, port) = SplitDataSource(builder.DataSource);
        return new SqlTargetSettings
        {
            Server = server,
            Port = port,
            Database = builder.InitialCatalog,
            Username = builder.UserID,
            Password = builder.Password,
            IntegratedSecurity = builder.IntegratedSecurity,
            Encrypt = builder.Encrypt,
            TrustServerCertificate = builder.TrustServerCertificate
        };
    }

    private static (string Server, int Port) SplitDataSource(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return (string.Empty, 1433);
        }

        var parts = dataSource.Split(',', 2);
        if (parts.Length == 2 && int.TryParse(parts[1], out var port))
        {
            return (parts[0], port);
        }

        return (dataSource, 1433);
    }
}
