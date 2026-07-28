using Microsoft.EntityFrameworkCore;

namespace Monitoring.Blazor.Services;

public sealed class EventTimelineService(IDbContextFactory<MonitoringDbContext> dbFactory)
{
    public async Task<List<EventTimelineEntry>> GetRecentEventsAsync(int take = 100, CancellationToken ct = default)
    {
        take = take <= 0 ? 100 : Math.Min(take, 500);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alerts = await db.AlertEvents
            .OrderByDescending(x => x.TimestampUtc)
            .Take(take)
            .Select(x => new EventTimelineEntry(
                x.TimestampUtc,
                "Alert",
                x.Hostname,
                x.Metric,
                x.Message,
                x.Type.ToString(),
                x.Value))
            .ToListAsync(ct);

        var loginAttempts = await db.MemberLoginAttempts
            .OrderByDescending(x => x.CreatedUtc)
            .Take(take)
            .Select(x => new EventTimelineEntry(
                x.CreatedUtc,
                "Login",
                x.UserName,
                x.Reason,
                x.Success ? "Success" : "Failed",
                x.IpAddress ?? string.Empty,
                x.MemberId ?? 0))
            .ToListAsync(ct);

        var audits = await db.MemberAuditLogs
            .OrderByDescending(x => x.CreatedUtc)
            .Take(take)
            .Select(x => new EventTimelineEntry(
                x.CreatedUtc,
                "Audit",
                x.ActorUserName,
                x.Action,
                x.Details ?? string.Empty,
                x.Success ? "Success" : "Failed",
                0))
            .ToListAsync(ct);

        return alerts
            .Concat(loginAttempts)
            .Concat(audits)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(take)
            .ToList();
    }
}

public sealed record EventTimelineEntry(
    DateTime TimestampUtc,
    string Category,
    string Subject,
    string Title,
    string Message,
    string Detail,
    double NumericValue);

