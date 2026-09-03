using Microsoft.EntityFrameworkCore;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : DbContext(options)
{
    public DbSet<HostSnapshotEntity> HostSnapshots => Set<HostSnapshotEntity>();
    public DbSet<AlertEventEntity> AlertEvents => Set<AlertEventEntity>();
    public DbSet<AlertSuppressionEntity> AlertSuppressions => Set<AlertSuppressionEntity>();
    public DbSet<DatabaseTableSizeRow> DatabaseTableSizes => Set<DatabaseTableSizeRow>();
    public DbSet<ApiMetricBucketEntity> ApiMetricBuckets => Set<ApiMetricBucketEntity>();
    public DbSet<LogIpDailyStatEntity> LogIpDailyStats => Set<LogIpDailyStatEntity>();
    public DbSet<MemberEntity> Members => Set<MemberEntity>();
    public DbSet<MemberAuditLogEntity> MemberAuditLogs => Set<MemberAuditLogEntity>();
    public DbSet<MemberLoginAttemptEntity> MemberLoginAttempts => Set<MemberLoginAttemptEntity>();
    public DbSet<MemberSessionEntity> MemberSessions => Set<MemberSessionEntity>();
    public DbSet<PasswordResetRequestEntity> PasswordResetRequests => Set<PasswordResetRequestEntity>();
    public DbSet<MemberRecoveryCodeEntity> MemberRecoveryCodes => Set<MemberRecoveryCodeEntity>();
    public DbSet<EmailVerificationRequestEntity> EmailVerificationRequests => Set<EmailVerificationRequestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DatabaseTableSizeRow>().HasNoKey();
        modelBuilder.Entity<DatabaseTableSizeRow>().Property(x => x.SizeMb).HasPrecision(12, 2);
        modelBuilder.Entity<ApiMetricBucketEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.BucketStartUtc, x.Path }).IsUnique();
            entity.Property(x => x.Path).HasMaxLength(500);
        });
        modelBuilder.Entity<HostSnapshotEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Hostname, x.CreatedUtc });
            entity.Property(x => x.Hostname).HasMaxLength(200);
            entity.Property(x => x.Ip).HasMaxLength(200);
            entity.Property(x => x.Os).HasMaxLength(200);
            entity.Property(x => x.TargetUrl).HasMaxLength(1000);
        });

        modelBuilder.Entity<AlertEventEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Hostname, x.TimestampUtc });
            entity.Property(x => x.Hostname).HasMaxLength(200);
            entity.Property(x => x.Ip).HasMaxLength(200);
            entity.Property(x => x.Os).HasMaxLength(200);
            entity.Property(x => x.Metric).HasMaxLength(200);
            entity.Property(x => x.AcknowledgedBy).HasMaxLength(100);
        });

        modelBuilder.Entity<AlertSuppressionEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Hostname, x.Metric }).IsUnique();
            entity.HasIndex(x => x.UntilUtc);
            entity.Property(x => x.Hostname).HasMaxLength(200);
            entity.Property(x => x.Metric).HasMaxLength(200);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.Kind).HasMaxLength(50);
        });

        modelBuilder.Entity<LogIpDailyStatEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerName, x.LogDate, x.Ip }).IsUnique();
            entity.HasIndex(x => new { x.ServerName, x.LogDate, x.RequestCount });
            entity.Property(x => x.ServerName).HasMaxLength(200);
            entity.Property(x => x.Ip).HasMaxLength(200);
        });

        modelBuilder.Entity<MemberEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserName).IsUnique();
            entity.Property(x => x.UserName).HasMaxLength(100);
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.EmailAddress).HasMaxLength(320);
            entity.Property(x => x.Role).HasMaxLength(50);
            entity.Property(x => x.PasswordHash).HasMaxLength(500);
            entity.Property(x => x.LastFailedLoginReason).HasMaxLength(200);
            entity.Property(x => x.LastFailedLoginIp).HasMaxLength(100);
            entity.Property(x => x.TwoFactorSecret).HasMaxLength(200);
        });

        modelBuilder.Entity<MemberAuditLogEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CreatedUtc, x.Action });
            entity.Property(x => x.ActorUserName).HasMaxLength(100);
            entity.Property(x => x.TargetUserName).HasMaxLength(100);
            entity.Property(x => x.Action).HasMaxLength(100);
            entity.Property(x => x.Details).HasMaxLength(2000);
        });

        modelBuilder.Entity<MemberLoginAttemptEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.CreatedUtc, x.UserName });
            entity.HasIndex(x => new { x.UserName, x.CreatedUtc });
            entity.Property(x => x.UserName).HasMaxLength(100);
            entity.Property(x => x.Reason).HasMaxLength(200);
            entity.Property(x => x.IpAddress).HasMaxLength(100);
            entity.Property(x => x.UserAgent).HasMaxLength(500);
        });

        modelBuilder.Entity<MemberSessionEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SessionId).IsUnique();
            entity.HasIndex(x => new { x.MemberId, x.ExpiresUtc });
            entity.Property(x => x.SessionId).HasMaxLength(100);
            entity.Property(x => x.IpAddress).HasMaxLength(100);
            entity.Property(x => x.UserAgent).HasMaxLength(500);
            entity.Property(x => x.RevokeReason).HasMaxLength(200);
        });

        modelBuilder.Entity<PasswordResetRequestEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserName, x.CreatedUtc });
            entity.Property(x => x.UserName).HasMaxLength(100);
            entity.Property(x => x.TokenHash).HasMaxLength(128);
            entity.Property(x => x.RequestedIp).HasMaxLength(100);
            entity.Property(x => x.RequestedUserAgent).HasMaxLength(500);
        });

        modelBuilder.Entity<MemberRecoveryCodeEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.MemberId, x.UsedUtc });
            entity.HasIndex(x => x.CodeHash).IsUnique();
            entity.Property(x => x.CodeHash).HasMaxLength(128);
        });

        modelBuilder.Entity<EmailVerificationRequestEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.MemberId, x.UsedUtc });
            entity.Property(x => x.EmailAddress).HasMaxLength(320);
            entity.Property(x => x.TokenHash).HasMaxLength(128);
        });
    }
}
