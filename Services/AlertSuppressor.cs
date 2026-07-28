using System.Collections.Concurrent;

namespace Monitoring.Blazor.Services;

public sealed class AlertSuppressor
{
    private readonly ConcurrentDictionary<string, SuppressionEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSuppressed(string host, string metric, DateTime now, out SuppressionEntry? entry)
    {
        entry = null;
        var key = BuildKey(host, metric);
        if (_entries.TryGetValue(key, out var existing))
        {
            if (existing.UntilUtc > now)
            {
                entry = existing;
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        return false;
    }

    public void Set(string host, string metric, DateTime untilUtc, string reason, string kind)
    {
        var key = BuildKey(host, metric);
        _entries[key] = new SuppressionEntry(host, metric, untilUtc, reason, kind);
    }

    public bool Clear(string host, string metric)
    {
        var key = BuildKey(host, metric);
        return _entries.TryRemove(key, out _);
    }

    public IReadOnlyCollection<SuppressionEntry> List() => _entries.Values.ToList();

    private static string BuildKey(string host, string metric) => $"{host}::{metric}";
}

public sealed record SuppressionEntry(
    string Hostname,
    string Metric,
    DateTime UntilUtc,
    string Reason,
    string Kind);
