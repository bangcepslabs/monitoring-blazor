using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class AlertSuppressor(IDbContextFactory<MonitoringDbContext> dbFactory)
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

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var entries = await db.AlertSuppressions.Where(x => x.UntilUtc > now).AsNoTracking().ToListAsync(ct);
        foreach (var entry in entries)
        {
            Set(entry.Hostname, entry.Metric, entry.UntilUtc, entry.Reason, entry.Kind);
        }
    }

    public async Task SetPersistedAsync(string host, string metric, DateTime untilUtc, string reason, string kind, CancellationToken ct = default)
    {
        Set(host, metric, untilUtc, reason, kind);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.AlertSuppressions.SingleOrDefaultAsync(x => x.Hostname == host && x.Metric == metric, ct);
        if (existing is null)
        {
            db.AlertSuppressions.Add(new AlertSuppressionEntity { Hostname = host, Metric = metric, UntilUtc = untilUtc, Reason = reason, Kind = kind, CreatedUtc = DateTime.UtcNow });
        }
        else
        {
            existing.UntilUtc = untilUtc;
            existing.Reason = reason;
            existing.Kind = kind;
        }
        await db.SaveChangesAsync(ct);
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
