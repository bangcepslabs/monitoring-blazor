namespace Monitoring.Blazor.Services;

public enum AlertType
{
    Threshold,
    Recovery,
    Anomaly
}

public sealed record AlertMessage(
    string Hostname,
    string Ip,
    string Os,
    string Metric,
    double Value,
    double Threshold,
    string Message,
    AlertType Type,
    DateTime TimestampUtc);

public sealed class AlertSettings
{
    public bool Enabled { get; set; } = false;
    public double CpuThreshold { get; set; } = 80;
    public int CpuDurationMinutes { get; set; } = 5;
    public double RamThreshold { get; set; } = 90;
    public double DiskThreshold { get; set; } = 95;
    public int CooldownMinutes { get; set; } = 10;
    public bool RecoveryEnabled { get; set; } = true;
    public int RecoveryCooldownMinutes { get; set; } = 10;
    public bool AnomalyEnabled { get; set; } = false;
    public int AnomalyWindow { get; set; } = 20;
    public double AnomalyStdDev { get; set; } = 2.0;
    public double AnomalyMinDelta { get; set; } = 10;
    public int AnomalyCooldownMinutes { get; set; } = 15;
    public bool MaintenanceWindowEnabled { get; set; } = false;
    public string MaintenanceWindowStart { get; set; } = "00:00";
    public string MaintenanceWindowEnd { get; set; } = "00:00";
}

public sealed class AlertSettingsOverride
{
    public bool? Enabled { get; init; }
    public double? CpuThreshold { get; init; }
    public int? CpuDurationMinutes { get; init; }
    public double? RamThreshold { get; init; }
    public double? DiskThreshold { get; init; }
    public int? CooldownMinutes { get; init; }
    public bool? RecoveryEnabled { get; init; }
    public int? RecoveryCooldownMinutes { get; init; }
    public bool? AnomalyEnabled { get; init; }
    public int? AnomalyWindow { get; init; }
    public double? AnomalyStdDev { get; init; }
    public double? AnomalyMinDelta { get; init; }
    public int? AnomalyCooldownMinutes { get; init; }
    public bool? MaintenanceWindowEnabled { get; init; }
    public string? MaintenanceWindowStart { get; init; }
    public string? MaintenanceWindowEnd { get; init; }
}
