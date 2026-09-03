using Monitoring.Blazor.Services;

namespace Monitoring.Blazor.Models;

public sealed class HostSnapshotEntity
{
    public long Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string? TargetUrl { get; set; }
    public int? Status { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double DiskUsage { get; set; }
    public double SentMbps { get; set; }
    public double RecvMbps { get; set; }
    public long BytesSent { get; set; }
    public long BytesRecv { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class AlertEventEntity
{
    public long Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Threshold { get; set; }
    public string Message { get; set; } = string.Empty;
    public AlertType Type { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public sealed class AlertSuppressionEntity
{
    public long Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public DateTime UntilUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public sealed class LogIpDailyStatEntity
{
    public long Id { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public DateOnly LogDate { get; set; }
    public string Ip { get; set; } = string.Empty;
    public long RequestCount { get; set; }
    public long Status2xxCount { get; set; }
    public long Status3xxCount { get; set; }
    public long Status4xxCount { get; set; }
    public long Status5xxCount { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}

public sealed class MemberEntity
{
    public long Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? EmailAddress { get; set; }
    public DateTime? EmailConfirmedUtc { get; set; }
    public string Role { get; set; } = "User";
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LastFailedLoginUtc { get; set; }
    public DateTime? LockoutUntilUtc { get; set; }
    public string? LastFailedLoginReason { get; set; }
    public string? LastFailedLoginIp { get; set; }
    public DateTime? PasswordChangedUtc { get; set; }
    public bool MustChangePassword { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }
    public DateTime? TwoFactorEnabledUtc { get; set; }
}

public sealed class MemberAuditLogEntity
{
    public long Id { get; set; }
    public string ActorUserName { get; set; } = string.Empty;
    public string? TargetUserName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public bool Success { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class MemberLoginAttemptEntity
{
    public long Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public long? MemberId { get; set; }
    public bool Success { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class MemberSessionEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public long MemberId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsPersistent { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? RevokeReason { get; set; }
}

public sealed class PasswordResetRequestEntity
{
    public long Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? UsedUtc { get; set; }
    public string? RequestedIp { get; set; }
    public string? RequestedUserAgent { get; set; }
}

public sealed class MemberRecoveryCodeEntity
{
    public long Id { get; set; }
    public long MemberId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? UsedUtc { get; set; }
}

public sealed class EmailVerificationRequestEntity
{
    public long Id { get; set; }
    public long MemberId { get; set; }
    public string EmailAddress { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? UsedUtc { get; set; }
}
