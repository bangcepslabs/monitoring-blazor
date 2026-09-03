using System.Collections.Concurrent;

namespace Monitoring.Blazor.Services;

public sealed record WorkerHealthSnapshot(
    DateTime? LastSuccessUtc,
    DateTime? LastFailureUtc,
    string? LastError);

public sealed class MonitoringHealthState
{
    private readonly ConcurrentDictionary<string, WorkerHealthSnapshot> _workers = new(StringComparer.OrdinalIgnoreCase);

    public void MarkSuccess(string workerName)
    {
        _workers.AddOrUpdate(
            workerName,
            _ => new WorkerHealthSnapshot(DateTime.UtcNow, null, null),
            (_, current) => current with { LastSuccessUtc = DateTime.UtcNow });
    }

    public void MarkFailure(string workerName, Exception exception)
    {
        _workers.AddOrUpdate(
            workerName,
            _ => new WorkerHealthSnapshot(null, DateTime.UtcNow, exception.Message),
            (_, current) => current with { LastFailureUtc = DateTime.UtcNow, LastError = exception.Message });
    }

    public WorkerHealthSnapshot Get(string workerName) =>
        _workers.TryGetValue(workerName, out var snapshot)
            ? snapshot
            : new WorkerHealthSnapshot(null, null, null);
}
