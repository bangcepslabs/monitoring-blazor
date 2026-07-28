using System.Security.Claims;
using Monitoring.Blazor.Models;
using Microsoft.EntityFrameworkCore;

namespace Monitoring.Blazor.Services;

public sealed class AuditLogService(
    IDbContextFactory<MonitoringDbContext> dbFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditLogService> logger)
{
    public async Task WriteChangeAsync(
        string action,
        string? targetUserName,
        IEnumerable<AuditChange> changes,
        bool success = true,
        string? summary = null,
        CancellationToken ct = default)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            lines.Add(summary.Trim());
        }

        foreach (var change in changes)
        {
            lines.Add($"{change.Field}: {FormatValue(change.Before)} -> {FormatValue(change.After)}");
        }

        await WriteAsync(action, targetUserName, success, string.Join(Environment.NewLine, lines), ct);
    }

    public async Task WriteAsync(string action, string? targetUserName, bool success, string? details, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.MemberAuditLogs.Add(new MemberAuditLogEntity
            {
                ActorUserName = ResolveActorUserName(),
                TargetUserName = string.IsNullOrWhiteSpace(targetUserName) ? null : targetUserName.Trim().ToLowerInvariant(),
                Action = action.Trim(),
                Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
                Success = success,
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write audit log for {Action}", action);
        }
    }

    public Task WriteSystemAsync(string action, string? targetUserName, bool success, string? details, CancellationToken ct = default)
    {
        return WriteAsync(action, targetUserName, success, details, ct);
    }

    private string ResolveActorUserName()
    {
        var userName = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(userName) ? "system" : userName.Trim().ToLowerInvariant();
    }

    private static string FormatValue(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
}

public sealed record AuditChange(string Field, string? Before, string? After);
