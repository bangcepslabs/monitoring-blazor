namespace Monitoring.Blazor.Services;

public sealed class SettingsBackupPayload
{
    public AlertSettings Alerts { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public LogAnalysisRuntimeSettings LogAnalysis { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public Dictionary<string, SqlTargetSettings> SqlTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ServerMetadata> ServerCatalog { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
