namespace Monitoring.Blazor.Services;

public sealed class SettingsBackupService(
    AlertSettingsRepository alertSettingsRepository,
    EmailSettingsRepository emailSettingsRepository,
    NotificationSettingsRepository notificationSettingsRepository,
    SecuritySettingsRepository securitySettingsRepository,
    SqlTargetSettingsRepository sqlTargetSettingsRepository,
    ServerCatalogRepository serverCatalogRepository,
    IConfiguration configuration)
{
    public SettingsBackupPayload CreateBackup()
    {
        var alerts = alertSettingsRepository.Load(configuration.GetSection("Monitoring:Alerts").Get<AlertSettings>() ?? new AlertSettings());
        var email = emailSettingsRepository.Load(configuration.GetSection("Monitoring:Email").Get<EmailSettings>() ?? new EmailSettings());
        var notifications = notificationSettingsRepository.Load(configuration.GetSection("Monitoring:Notifications").Get<NotificationSettings>() ?? new NotificationSettings());
        var security = securitySettingsRepository.Load(configuration.GetSection("Monitoring:Security").Get<SecuritySettings>() ?? new SecuritySettings());
        var sqlTargets = sqlTargetSettingsRepository.LoadAll();
        var serverCatalog = serverCatalogRepository.LoadAll();

        return new SettingsBackupPayload
        {
            Alerts = alerts,
            Email = email,
            Notifications = notifications,
            Security = security,
            SqlTargets = sqlTargets,
            ServerCatalog = serverCatalog
        };
    }

    public void Restore(SettingsBackupPayload payload)
    {
        alertSettingsRepository.Save(payload.Alerts ?? new AlertSettings());
        emailSettingsRepository.Save(payload.Email ?? new EmailSettings());
        notificationSettingsRepository.Save(payload.Notifications ?? new NotificationSettings());
        securitySettingsRepository.Save(payload.Security ?? new SecuritySettings());

        sqlTargetSettingsRepository.ReplaceAll(new Dictionary<string, SqlTargetSettings>(
            payload.SqlTargets ?? new Dictionary<string, SqlTargetSettings>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase));

        var currentServers = serverCatalogRepository.LoadAll();
        foreach (var key in currentServers.Keys.ToList())
        {
            serverCatalogRepository.Remove(key);
        }

        foreach (var entry in payload.ServerCatalog ?? new Dictionary<string, ServerMetadata>(StringComparer.OrdinalIgnoreCase))
        {
            serverCatalogRepository.Save(entry.Key, entry.Value ?? new ServerMetadata());
        }
    }
}
