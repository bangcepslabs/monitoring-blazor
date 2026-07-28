using System.Collections.Concurrent;
using System.Text.Json;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class MonitorStateService(AlertEvaluator alertEvaluator, MonitoringSnapshotQueue snapshotQueue)
{
    private readonly ConcurrentDictionary<string, HostState> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public event Action? Changed;

    public void UpdateServer(MonitoringInfo info) => Upsert(info);

    public bool TryUpdateClientFromJson(string json)
    {
        var info = JsonSerializer.Deserialize<MonitoringInfo>(json, _jsonOptions);
        if (info is null || string.IsNullOrWhiteSpace(info.Hostname))
        {
            return false;
        }

        Upsert(info);
        return true;
    }

    public IReadOnlyList<HostState> GetSnapshot(TimeSpan offlineThreshold)
    {
        var now = DateTime.UtcNow;
        return _hosts.Values
            .Select(item =>
            {
                item.IsOnline = (now - item.LastUpdatedUtc) < offlineThreshold;
                return item;
            })
            .OrderBy(x => x.Info.Hostname, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Upsert(MonitoringInfo info)
    {
        var now = DateTime.UtcNow;
        _hosts.AddOrUpdate(
            info.Hostname,
            _ => new HostState
            {
                Info = info,
                LastUpdatedUtc = now,
                IsOnline = true
            },
            (_, existing) =>
            {
                existing.Info = info;
                existing.LastUpdatedUtc = now;
                existing.IsOnline = true;
                return existing;
            });

        snapshotQueue.Enqueue(info);
        alertEvaluator.Evaluate(info);
        Changed?.Invoke();
    }
}
