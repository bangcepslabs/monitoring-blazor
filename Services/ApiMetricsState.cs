using System.Collections.Concurrent;

namespace Monitoring.Blazor.Services;

public sealed record ApiMetricSnapshot(string Path, long Requests, long Errors, double AverageMs);

public sealed class ApiMetricsState
{
    private readonly object _sync = new();
    private sealed class Counter { public long Requests; public long Errors; public long TotalMs; }
    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string path, int statusCode, double elapsedMs)
    {
        lock (_sync)
        {
            var counter = _counters.GetOrAdd(path, _ => new Counter());
            Interlocked.Increment(ref counter.Requests);
            if (statusCode >= 400) Interlocked.Increment(ref counter.Errors);
            Interlocked.Add(ref counter.TotalMs, (long)Math.Round(elapsedMs));
        }
    }

    public IReadOnlyList<ApiMetricSnapshot> GetSnapshot() => _counters.Select(pair =>
    {
        var counter = pair.Value;
        var requests = Interlocked.Read(ref counter.Requests);
        return new ApiMetricSnapshot(pair.Key, requests, Interlocked.Read(ref counter.Errors), requests == 0 ? 0 : (double)Interlocked.Read(ref counter.TotalMs) / requests);
    }).OrderByDescending(x => x.Requests).ToList();

    public IReadOnlyList<ApiMetricSnapshot> TakeSnapshotAndReset()
    {
        lock (_sync)
        {
            var snapshot = GetSnapshot();
            _counters.Clear();
            return snapshot;
        }
    }
}
