using System.Collections.Concurrent;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class AlertEvaluator(
    IConfiguration configuration,
    AlertDispatcher dispatcher,
    AlertSuppressor suppressor,
    AlertSettingsRepository settingsRepo,
    ILogger<AlertEvaluator> logger)
{
    private readonly ConcurrentDictionary<string, AlertState> _states = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, AlertSettingsOverride>? _overrides;
    private AlertSettings? _cachedRepoSettings;

    public void Evaluate(MonitoringInfo info)
    {
        var baseSettings = _cachedRepoSettings ??= settingsRepo.Load(
            configuration.GetSection("Monitoring:Alerts").Get<AlertSettings>() ?? new AlertSettings());
        var overrides = _overrides ??= configuration.GetSection("Monitoring:Alerts:Overrides").Get<Dictionary<string, AlertSettingsOverride>>() ?? new Dictionary<string, AlertSettingsOverride>(StringComparer.OrdinalIgnoreCase);
        var settings = MergeSettings(baseSettings, overrides.TryGetValue(info.Hostname, out var o) ? o : null);
        if (!settings.Enabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (IsMaintenanceWindowActive(settings, now))
        {
            logger.LogDebug("Alert evaluation skipped for {Host} because maintenance window is active.", info.Hostname);
            return;
        }

        var state = _states.GetOrAdd(info.Hostname, _ => new AlertState());

        var cpu = info.Dynamic.CpuInfo.Usage;
        var mem = info.Dynamic.MemoryInfo.Usage;
        var disk = info.Dynamic.DiskInfo.Percent;

        // CPU: threshold must be sustained for duration.
        if (cpu >= settings.CpuThreshold)
        {
            state.CpuBreachStartUtc ??= now;
            var elapsed = now - state.CpuBreachStartUtc.Value;
            if (elapsed.TotalMinutes >= settings.CpuDurationMinutes &&
                IsCooldownPassed(state.LastCpuAlertUtc, now, settings.CooldownMinutes) &&
                !IsSuppressed(info.Hostname, "CPU", now))
            {
                dispatcher.Enqueue(BuildMessage(info, "CPU", cpu, settings.CpuThreshold,
                    $"CPU {cpu:0.0}% >= {settings.CpuThreshold:0.0}% for {settings.CpuDurationMinutes} minutes",
                    AlertType.Threshold));
                state.LastCpuAlertUtc = now;
            }
        }
        else
        {
            if (settings.RecoveryEnabled && state.CpuBreachStartUtc is not null &&
                IsCooldownPassed(state.LastCpuRecoveryUtc, now, settings.RecoveryCooldownMinutes) &&
                !IsSuppressed(info.Hostname, "CPU", now))
            {
                dispatcher.Enqueue(BuildMessage(info, "CPU", cpu, settings.CpuThreshold,
                    $"CPU recovered to {cpu:0.0}% (< {settings.CpuThreshold:0.0}%)",
                    AlertType.Recovery));
                state.LastCpuRecoveryUtc = now;
            }
            state.CpuBreachStartUtc = null;
        }

        // RAM: immediate threshold.
        if (mem >= settings.RamThreshold &&
            IsCooldownPassed(state.LastRamAlertUtc, now, settings.CooldownMinutes) &&
            !IsSuppressed(info.Hostname, "RAM", now))
        {
            dispatcher.Enqueue(BuildMessage(info, "RAM", mem, settings.RamThreshold,
                $"RAM {mem:0.0}% >= {settings.RamThreshold:0.0}%",
                AlertType.Threshold));
            state.LastRamAlertUtc = now;
            state.RamBreached = true;
        }
        else if (settings.RecoveryEnabled && state.RamBreached && mem < settings.RamThreshold &&
                 IsCooldownPassed(state.LastRamRecoveryUtc, now, settings.RecoveryCooldownMinutes) &&
                 !IsSuppressed(info.Hostname, "RAM", now))
        {
            dispatcher.Enqueue(BuildMessage(info, "RAM", mem, settings.RamThreshold,
                $"RAM recovered to {mem:0.0}% (< {settings.RamThreshold:0.0}%)",
                AlertType.Recovery));
            state.LastRamRecoveryUtc = now;
            state.RamBreached = false;
        }

        // Disk: immediate threshold.
        if (disk >= settings.DiskThreshold &&
            IsCooldownPassed(state.LastDiskAlertUtc, now, settings.CooldownMinutes) &&
            !IsSuppressed(info.Hostname, "DISK", now))
        {
            dispatcher.Enqueue(BuildMessage(info, "DISK", disk, settings.DiskThreshold,
                $"DISK {disk:0.0}% >= {settings.DiskThreshold:0.0}%",
                AlertType.Threshold));
            state.LastDiskAlertUtc = now;
            state.DiskBreached = true;
        }
        else if (settings.RecoveryEnabled && state.DiskBreached && disk < settings.DiskThreshold &&
                 IsCooldownPassed(state.LastDiskRecoveryUtc, now, settings.RecoveryCooldownMinutes) &&
                 !IsSuppressed(info.Hostname, "DISK", now))
        {
            dispatcher.Enqueue(BuildMessage(info, "DISK", disk, settings.DiskThreshold,
                $"DISK recovered to {disk:0.0}% (< {settings.DiskThreshold:0.0}%)",
                AlertType.Recovery));
            state.LastDiskRecoveryUtc = now;
            state.DiskBreached = false;
        }

        // Anomaly detection (spike vs rolling stats).
        if (settings.AnomalyEnabled)
        {
            EvaluateAnomaly("CPU", cpu, state.CpuHistory, settings, info, now);
            EvaluateAnomaly("RAM", mem, state.RamHistory, settings, info, now);
            EvaluateAnomaly("DISK", disk, state.DiskHistory, settings, info, now);
        }

        logger.LogDebug("Alert evaluation completed for {Host}", info.Hostname);
    }

    private bool IsSuppressed(string host, string metric, DateTime now)
    {
        return suppressor.IsSuppressed(host, metric, now, out _);
    }

    private static bool IsCooldownPassed(DateTime? last, DateTime now, int cooldownMinutes)
    {
        if (last is null)
        {
            return true;
        }

        return (now - last.Value).TotalMinutes >= cooldownMinutes;
    }

    private static AlertMessage BuildMessage(MonitoringInfo info, string metric, double value, double threshold, string reason, AlertType type)
    {
        return new AlertMessage(
            info.Hostname,
            info.Ip,
            info.Os,
            metric,
            value,
            threshold,
            reason,
            type,
            DateTime.UtcNow);
    }

    private static AlertSettings MergeSettings(AlertSettings baseSettings, AlertSettingsOverride? overrideSettings)
    {
        if (overrideSettings is null)
        {
            return baseSettings;
        }

        return new AlertSettings
        {
            Enabled = overrideSettings.Enabled ?? baseSettings.Enabled,
            CpuThreshold = overrideSettings.CpuThreshold ?? baseSettings.CpuThreshold,
            CpuDurationMinutes = overrideSettings.CpuDurationMinutes ?? baseSettings.CpuDurationMinutes,
            RamThreshold = overrideSettings.RamThreshold ?? baseSettings.RamThreshold,
            DiskThreshold = overrideSettings.DiskThreshold ?? baseSettings.DiskThreshold,
            CooldownMinutes = overrideSettings.CooldownMinutes ?? baseSettings.CooldownMinutes,
            RecoveryEnabled = overrideSettings.RecoveryEnabled ?? baseSettings.RecoveryEnabled,
            RecoveryCooldownMinutes = overrideSettings.RecoveryCooldownMinutes ?? baseSettings.RecoveryCooldownMinutes,
            AnomalyEnabled = overrideSettings.AnomalyEnabled ?? baseSettings.AnomalyEnabled,
            AnomalyWindow = overrideSettings.AnomalyWindow ?? baseSettings.AnomalyWindow,
            AnomalyStdDev = overrideSettings.AnomalyStdDev ?? baseSettings.AnomalyStdDev,
            AnomalyMinDelta = overrideSettings.AnomalyMinDelta ?? baseSettings.AnomalyMinDelta,
            AnomalyCooldownMinutes = overrideSettings.AnomalyCooldownMinutes ?? baseSettings.AnomalyCooldownMinutes,
            MaintenanceWindowEnabled = overrideSettings.MaintenanceWindowEnabled ?? baseSettings.MaintenanceWindowEnabled,
            MaintenanceWindowStart = overrideSettings.MaintenanceWindowStart ?? baseSettings.MaintenanceWindowStart,
            MaintenanceWindowEnd = overrideSettings.MaintenanceWindowEnd ?? baseSettings.MaintenanceWindowEnd
        };
    }

    private static bool IsMaintenanceWindowActive(AlertSettings settings, DateTime nowUtc)
    {
        if (!settings.MaintenanceWindowEnabled)
        {
            return false;
        }

        if (!TimeSpan.TryParse(settings.MaintenanceWindowStart, out var start) ||
            !TimeSpan.TryParse(settings.MaintenanceWindowEnd, out var end))
        {
            return false;
        }

        if (start == end)
        {
            return false;
        }

        var localTime = nowUtc.ToLocalTime().TimeOfDay;
        if (start < end)
        {
            return localTime >= start && localTime < end;
        }

        return localTime >= start || localTime < end;
    }

    private void EvaluateAnomaly(string metric, double value, Queue<double> history, AlertSettings settings, MonitoringInfo info, DateTime now)
    {
        if (history.Count >= settings.AnomalyWindow)
        {
            var stats = ComputeStats(history);
            var threshold = stats.Mean + settings.AnomalyStdDev * stats.StdDev;
            var delta = value - stats.Mean;
            if (value > threshold && delta >= settings.AnomalyMinDelta &&
                IsCooldownPassed(GetLastAnomaly(info.Hostname, metric), now, settings.AnomalyCooldownMinutes) &&
                !IsSuppressed(info.Hostname, metric, now))
            {
                dispatcher.Enqueue(BuildMessage(info, metric, value, threshold,
                    $"{metric} anomaly: {value:0.0}% > {threshold:0.0}% (mean {stats.Mean:0.0}%, std {stats.StdDev:0.0})",
                    AlertType.Anomaly));
                SetLastAnomaly(info.Hostname, metric, now);
            }
        }

        history.Enqueue(value);
        while (history.Count > settings.AnomalyWindow)
        {
            history.Dequeue();
        }
    }

    private DateTime? GetLastAnomaly(string host, string metric)
    {
        if (!_states.TryGetValue(host, out var state))
        {
            return null;
        }

        return metric switch
        {
            "CPU" => state.LastCpuAnomalyUtc,
            "RAM" => state.LastRamAnomalyUtc,
            "DISK" => state.LastDiskAnomalyUtc,
            _ => null
        };
    }

    private void SetLastAnomaly(string host, string metric, DateTime now)
    {
        if (!_states.TryGetValue(host, out var state))
        {
            return;
        }

        switch (metric)
        {
            case "CPU":
                state.LastCpuAnomalyUtc = now;
                break;
            case "RAM":
                state.LastRamAnomalyUtc = now;
                break;
            case "DISK":
                state.LastDiskAnomalyUtc = now;
                break;
        }
    }

    private static (double Mean, double StdDev) ComputeStats(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0)
        {
            return (0, 0);
        }

        var mean = list.Average();
        var variance = list.Select(v => (v - mean) * (v - mean)).Average();
        return (mean, Math.Sqrt(variance));
    }

    private sealed class AlertState
    {
        public DateTime? CpuBreachStartUtc { get; set; }
        public DateTime? LastCpuAlertUtc { get; set; }
        public DateTime? LastCpuRecoveryUtc { get; set; }
        public DateTime? LastCpuAnomalyUtc { get; set; }
        public DateTime? LastRamAlertUtc { get; set; }
        public DateTime? LastRamRecoveryUtc { get; set; }
        public DateTime? LastRamAnomalyUtc { get; set; }
        public DateTime? LastDiskAlertUtc { get; set; }
        public DateTime? LastDiskRecoveryUtc { get; set; }
        public DateTime? LastDiskAnomalyUtc { get; set; }
        public bool RamBreached { get; set; }
        public bool DiskBreached { get; set; }
        public Queue<double> CpuHistory { get; } = new();
        public Queue<double> RamHistory { get; } = new();
        public Queue<double> DiskHistory { get; } = new();
    }
}
