using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Monitoring.Blazor.Models;
using QRCoder;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;

namespace Monitoring.Blazor.Services;

public sealed record LoginResult(
    bool Succeeded,
    MemberEntity? Member,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsLocked);

public sealed class MemberAuthService(
    IDbContextFactory<MonitoringDbContext> dbFactory,
    IPasswordHasher<MemberEntity> passwordHasher,
    IHttpContextAccessor httpContextAccessor,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    EmailSettingsRepository emailSettingsRepository,
    AuditLogService auditLogService)
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan DefaultLockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultSessionDuration = TimeSpan.FromHours(8);
    private static readonly TimeSpan PersistentSessionDuration = TimeSpan.FromDays(30);
    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EmailVerificationTokenLifetime = TimeSpan.FromHours(24);
    private const int TwoFactorStepSeconds = 30;
    private const int TwoFactorDigits = 6;
    private readonly IDataProtector _twoFactorProtector = dataProtectionProvider.CreateProtector("Monitoring.Blazor.MemberAuthService.TwoFactorSecret");

    public async Task<bool> HasMembersAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Members.AnyAsync(ct);
    }

    public async Task<MemberEntity?> FindActiveMemberAsync(string userName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var normalized = NormalizeUserName(userName);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Members.FirstOrDefaultAsync(x => x.IsActive && x.UserName == normalized, ct);
    }

    public async Task<MemberEntity> CreateBootstrapMemberAsync(
        string userName,
        string displayName,
        string password,
        string role = "Admin",
        string? emailAddress = null,
        CancellationToken ct = default)
    {
        var member = await CreateMemberCoreAsync(userName, displayName, password, role, emailAddress, ct);
        await WriteAuditAsync("member_create", member.UserName, true, $"role={member.Role}; bootstrap=true", ct);
        return member;
    }

    public async Task<MemberEntity> RegisterMemberAsync(
        string userName,
        string displayName,
        string password,
        string? emailAddress = null,
        CancellationToken ct = default)
    {
        var member = await CreateMemberCoreAsync(userName, displayName, password, "User", emailAddress, ct);
        await WriteAuditAsync("member_register", member.UserName, true, "role=User", ct);
        return member;
    }

    public async Task<MemberEntity> CreateMemberAsync(
        string userName,
        string displayName,
        string password,
        string role,
        string? emailAddress = null,
        CancellationToken ct = default)
    {
        var member = await CreateMemberCoreAsync(userName, displayName, password, role, emailAddress, ct);
        await WriteAuditAsync("member_create", member.UserName, true, $"role={member.Role}", ct);
        return member;
    }

    public async Task<MemberEntity?> ValidatePasswordAsync(string userName, string password, CancellationToken ct = default)
    {
        var member = await FindActiveMemberAsync(userName, ct);
        if (member is null)
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(member, member.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : member;
    }

    public async Task<LoginResult> AuthenticateAsync(string userName, string password, CancellationToken ct = default)
    {
        var normalizedUserName = NormalizeUserName(userName);
        var nowUtc = DateTime.UtcNow;
        var requestIp = ResolveRequestIpAddress();
        var requestUserAgent = ResolveRequestUserAgent();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.UserName == normalizedUserName, ct);
        if (member is null)
        {
            await WriteLoginAttemptAsync(db, normalizedUserName, null, false, "user_not_found", requestIp, requestUserAgent, ct);
            return new LoginResult(false, null, "invalid", "Invalid username or password.", false);
        }

        if (!member.IsActive)
        {
            await WriteLoginAttemptAsync(db, member.UserName, member.Id, false, "inactive", requestIp, requestUserAgent, ct);
            return new LoginResult(false, null, "inactive", "This account is inactive.", false);
        }

        if (member.LockoutUntilUtc is not null && member.LockoutUntilUtc > nowUtc)
        {
            await WriteLoginAttemptAsync(db, member.UserName, member.Id, false, "locked", requestIp, requestUserAgent, ct);
            return new LoginResult(false, null, "locked", $"This account is locked until {member.LockoutUntilUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}.", true);
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(member, member.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            member.FailedLoginCount = Math.Min(member.FailedLoginCount + 1, int.MaxValue);
            member.LastFailedLoginUtc = nowUtc;
            member.LastFailedLoginReason = "invalid_password";
            member.LastFailedLoginIp = requestIp;
            if (member.FailedLoginCount >= MaxFailedLoginAttempts)
            {
                member.LockoutUntilUtc = nowUtc.Add(DefaultLockoutDuration);
                member.LastFailedLoginReason = "lockout_threshold_reached";
            }

            await db.SaveChangesAsync(ct);
            await WriteLoginAttemptAsync(db, member.UserName, member.Id, false, member.LockoutUntilUtc is not null && member.LockoutUntilUtc > nowUtc ? "locked" : "invalid_password", requestIp, requestUserAgent, ct);

            if (member.LockoutUntilUtc is not null && member.LockoutUntilUtc > nowUtc)
            {
                return new LoginResult(false, null, "locked", $"This account is locked until {member.LockoutUntilUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}.", true);
            }

            return new LoginResult(false, null, "invalid", "Invalid username or password.", false);
        }

        member.FailedLoginCount = 0;
        member.LockoutUntilUtc = null;
        member.LastFailedLoginReason = null;
        member.LastFailedLoginIp = null;
        member.LastFailedLoginUtc = null;
        await db.SaveChangesAsync(ct);

        var emailSettings = GetEmailSettings();
        if (emailSettings.Enabled && emailSettings.VerificationEnabled &&
            !string.IsNullOrWhiteSpace(member.EmailAddress) &&
            member.EmailConfirmedUtc is null)
        {
            await WriteLoginAttemptAsync(db, member.UserName, member.Id, false, "email_unverified", requestIp, requestUserAgent, ct);
            return new LoginResult(false, null, "email_unverified", "Please verify your email address before signing in.", false);
        }

        await WriteLoginAttemptAsync(db, member.UserName, member.Id, true, "ok", requestIp, requestUserAgent, ct);
        return new LoginResult(true, member, null, null, false);
    }

    public async Task<bool> RequiresTwoFactorAsync(string userName, CancellationToken ct = default)
    {
        var normalized = NormalizeUserName(userName);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Members.AnyAsync(x => x.UserName == normalized && x.IsActive && x.TwoFactorEnabled, ct);
    }

    public async Task<string> BeginTwoFactorSetupAsync(long memberId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            throw new InvalidOperationException("Member not found.");
        }

        var secret = GenerateTwoFactorSecret();
        member.TwoFactorSecret = ProtectTwoFactorSecret(secret);
        member.TwoFactorEnabled = false;
        member.TwoFactorEnabledUtc = null;
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("two_factor_setup_start", member.UserName, true, "2fa secret generated", ct);
        return secret;
    }

    public async Task EnableTwoFactorAsync(long memberId, string code, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            throw new InvalidOperationException("Member not found.");
        }

        var secret = UnprotectTwoFactorSecret(member.TwoFactorSecret);
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Two-factor setup has not been started.");
        }

        if (!VerifyTwoFactorCode(secret, code))
        {
            await WriteAuditAsync("two_factor_setup_confirm", member.UserName, false, "invalid code", ct);
            throw new InvalidOperationException("Two-factor verification code is invalid.");
        }

        member.TwoFactorEnabled = true;
        member.TwoFactorEnabledUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("two_factor_setup_confirm", member.UserName, true, "2fa enabled", ct);
    }

    public async Task DisableTwoFactorAsync(long memberId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            return;
        }

        member.TwoFactorEnabled = false;
        member.TwoFactorSecret = null;
        member.TwoFactorEnabledUtc = null;
        var activeRecoveryCodes = await db.MemberRecoveryCodes.Where(x => x.MemberId == memberId && x.UsedUtc == null).ToListAsync(ct);
        db.MemberRecoveryCodes.RemoveRange(activeRecoveryCodes);
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("two_factor_disabled", member.UserName, true, "2fa disabled", ct);
    }

    public async Task<bool> VerifyTwoFactorCodeAsync(long memberId, string code, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        var secret = member is null ? null : UnprotectTwoFactorSecret(member.TwoFactorSecret);
        if (member is null || !member.IsActive || !member.TwoFactorEnabled || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var success = VerifyTwoFactorCode(secret, code);
        await WriteAuditAsync("two_factor_verify", member.UserName, success, success ? "2fa ok" : "invalid 2fa code", ct);
        return success;
    }

    public async Task<bool> VerifyTwoFactorRecoveryCodeAsync(long memberId, string code, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null || !member.IsActive || !member.TwoFactorEnabled)
        {
            return false;
        }

        var normalized = NormalizeRecoveryCode(code);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var codeHash = HashToken(normalized);
        var recoveryCode = await db.MemberRecoveryCodes.FirstOrDefaultAsync(x =>
            x.MemberId == memberId && x.CodeHash == codeHash && x.UsedUtc == null, ct);

        if (recoveryCode is null)
        {
            await WriteAuditAsync("two_factor_recovery", member.UserName, false, "invalid recovery code", ct);
            return false;
        }

        recoveryCode.UsedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("two_factor_recovery", member.UserName, true, "recovery code used", ct);
        return true;
    }

    public async Task<List<string>> GenerateRecoveryCodesAsync(long memberId, int count = 8, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            throw new InvalidOperationException("Member not found.");
        }

        var codeCount = count <= 0 ? 8 : Math.Min(count, 16);
        var nowUtc = DateTime.UtcNow;
        var codes = new List<string>(codeCount);

        for (var i = 0; i < codeCount; i++)
        {
            var code = GenerateRecoveryCode();
            codes.Add(code);
            db.MemberRecoveryCodes.Add(new MemberRecoveryCodeEntity
            {
                MemberId = memberId,
                CodeHash = HashToken(NormalizeRecoveryCode(code)),
                CreatedUtc = nowUtc
            });
        }

        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("two_factor_recovery_generate", member.UserName, true, $"count={codes.Count}", ct);
        return codes;
    }

    public async Task<string> CreateSessionAsync(MemberEntity member, bool rememberMe, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sessionId = GenerateSessionId();
        var nowUtc = DateTime.UtcNow;
        var expiresUtc = nowUtc.Add(rememberMe ? PersistentSessionDuration : DefaultSessionDuration);

        db.MemberSessions.Add(new MemberSessionEntity
        {
            SessionId = sessionId,
            MemberId = member.Id,
            IpAddress = ResolveRequestIpAddress(),
            UserAgent = ResolveRequestUserAgent(),
            IsPersistent = rememberMe,
            CreatedUtc = nowUtc,
            LastSeenUtc = nowUtc,
            ExpiresUtc = expiresUtc
        });
        await db.SaveChangesAsync(ct);
        return sessionId;
    }

    public async Task<bool> IsSessionValidAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var session = await db.MemberSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        if (session is null)
        {
            return false;
        }

        if (session.RevokedUtc is not null || session.ExpiresUtc <= nowUtc)
        {
            return false;
        }

        session.LastSeenUtc = nowUtc;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<MemberSessionEntity>> GetActiveSessionsAsync(long memberId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;
        return await db.MemberSessions
            .Where(x => x.MemberId == memberId && x.RevokedUtc == null && x.ExpiresUtc > nowUtc)
            .OrderByDescending(x => x.LastSeenUtc)
            .ToListAsync(ct);
    }

    public async Task RevokeSessionAsync(string sessionId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.MemberSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        if (session is null)
        {
            return;
        }

        session.RevokedUtc = DateTime.UtcNow;
        session.RevokeReason = string.IsNullOrWhiteSpace(reason) ? "revoked" : reason.Trim();
        await db.SaveChangesAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == session.MemberId, ct);
        if (member is not null)
        {
            await WriteAuditAsync("session_revoke", member.UserName, true, session.RevokeReason, ct);
        }
    }

    public async Task RevokeAllSessionsAsync(long memberId, string reason = "revoked all", CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var sessions = await db.MemberSessions
            .Where(x => x.MemberId == memberId && x.RevokedUtc == null && x.ExpiresUtc > nowUtc)
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            session.RevokedUtc = nowUtc;
            session.RevokeReason = reason;
        }

        await db.SaveChangesAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is not null)
        {
            await WriteAuditAsync("session_revoke_all", member.UserName, true, reason, ct);
        }
    }

    public async Task<(string Token, DateTime ExpiresUtc)> CreatePasswordResetRequestAsync(string userName, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalizedUserName = NormalizeUserName(userName);
        var memberExists = await db.Members.AnyAsync(x => x.UserName == normalizedUserName && x.IsActive, ct);
        if (!memberExists)
        {
            throw new InvalidOperationException("Member not found.");
        }

        var token = GenerateResetToken();
        var nowUtc = DateTime.UtcNow;
        var expiresUtc = nowUtc.Add(PasswordResetTokenLifetime);

        db.PasswordResetRequests.Add(new PasswordResetRequestEntity
        {
            UserName = normalizedUserName,
            TokenHash = HashToken(token),
            CreatedUtc = nowUtc,
            ExpiresUtc = expiresUtc,
            RequestedIp = ResolveRequestIpAddress(),
            RequestedUserAgent = ResolveRequestUserAgent()
        });
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("password_reset_request", normalizedUserName, true, "reset token generated", ct);
        return (token, expiresUtc);
    }

    public async Task<(string Token, DateTime ExpiresUtc, string EmailAddress, string DisplayName)> CreatePasswordResetRequestByEmailAsync(string emailAddress, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalizedEmail = NormalizeEmailAddress(emailAddress);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new InvalidOperationException("Email address is required.");
        }

        var member = await db.Members.FirstOrDefaultAsync(x =>
            x.IsActive &&
            x.EmailAddress != null &&
            x.EmailAddress == normalizedEmail, ct);

        if (member is null)
        {
            throw new InvalidOperationException("Member not found.");
        }

        var recentRequestExists = await db.PasswordResetRequests.AnyAsync(x =>
            x.UserName == member.UserName &&
            x.UsedUtc == null &&
            x.CreatedUtc > DateTime.UtcNow.AddMinutes(-5), ct);
        if (recentRequestExists)
        {
            throw new InvalidOperationException("Please wait a few minutes before requesting another reset link.");
        }

        var token = GenerateResetToken();
        var nowUtc = DateTime.UtcNow;
        var expiresUtc = nowUtc.Add(PasswordResetTokenLifetime);

        db.PasswordResetRequests.Add(new PasswordResetRequestEntity
        {
            UserName = member.UserName,
            TokenHash = HashToken(token),
            CreatedUtc = nowUtc,
            ExpiresUtc = expiresUtc,
            RequestedIp = ResolveRequestIpAddress(),
            RequestedUserAgent = ResolveRequestUserAgent()
        });
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("password_reset_request", member.UserName, true, "reset token generated via email", ct);
        return (token, expiresUtc, normalizedEmail, member.DisplayName);
    }

    public async Task<string> CreateEmailVerificationRequestAsync(long memberId, string emailAddress, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            throw new InvalidOperationException("Member not found.");
        }

        var normalizedEmail = NormalizeEmailAddress(emailAddress);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new InvalidOperationException("Email address is required.");
        }

        var token = GenerateResetToken();
        var nowUtc = DateTime.UtcNow;
        member.EmailAddress = normalizedEmail;
        member.EmailConfirmedUtc = null;
        db.EmailVerificationRequests.Add(new EmailVerificationRequestEntity
        {
            MemberId = member.Id,
            EmailAddress = normalizedEmail,
            TokenHash = HashToken(token),
            CreatedUtc = nowUtc,
            ExpiresUtc = nowUtc.Add(EmailVerificationTokenLifetime)
        });

        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("email_verification_request", member.UserName, true, "verification token generated", ct);
        return token;
    }

    public async Task<bool> ConfirmEmailVerificationAsync(string token, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalizedToken = NormalizeVerificationToken(token);
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return false;
        }

        var tokenHash = HashToken(normalizedToken);
        var nowUtc = DateTime.UtcNow;
        var request = await db.EmailVerificationRequests.FirstOrDefaultAsync(x =>
            x.TokenHash == tokenHash && x.UsedUtc == null && x.ExpiresUtc > nowUtc, ct);
        if (request is null)
        {
            return false;
        }

        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == request.MemberId, ct);
        if (member is null || !member.IsActive)
        {
            return false;
        }

        request.UsedUtc = nowUtc;
        member.EmailAddress = request.EmailAddress;
        member.EmailConfirmedUtc = nowUtc;
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("email_verified", member.UserName, true, "email verified", ct);
        return true;
    }

    public async Task ResetPasswordWithTokenAsync(string token, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Reset token is required.");
        }

        var passwordError = ValidatePasswordPolicy(string.Empty, newPassword);
        if (passwordError is not null)
        {
            throw new InvalidOperationException(passwordError);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tokenHash = HashToken(token);
        var nowUtc = DateTime.UtcNow;
        var request = await db.PasswordResetRequests
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.UsedUtc == null && x.ExpiresUtc > nowUtc, ct);

        if (request is null)
        {
            throw new InvalidOperationException("Reset token is invalid or expired.");
        }

        var member = await db.Members.FirstOrDefaultAsync(x => x.UserName == request.UserName, ct);
        if (member is null)
        {
            throw new InvalidOperationException("Member not found.");
        }

        member.PasswordHash = passwordHasher.HashPassword(member, newPassword);
        member.PasswordChangedUtc = nowUtc;
        member.MustChangePassword = false;
        request.UsedUtc = nowUtc;
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync("password_reset_confirm", member.UserName, true, "password reset by token", ct);
    }

    public async Task<List<MemberEntity>> GetMembersAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Members
            .OrderBy(x => x.UserName)
            .ToListAsync(ct);
    }

    public async Task<MemberEntity?> GetMemberByIdAsync(long memberId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
    }

    public async Task SetMemberActiveAsync(long memberId, bool isActive, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            return;
        }

        if (!isActive && string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var activeAdmins = await db.Members.CountAsync(x => x.IsActive && x.Role == "Admin", ct);
            if (activeAdmins <= 1)
            {
                throw new InvalidOperationException("At least one active admin must remain.");
            }
        }

        var before = member.IsActive;
        member.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteChangeAsync(
            "member_active",
            member.UserName,
            [new AuditChange("IsActive", before.ToString(), isActive.ToString())],
            true,
            "Member active status updated",
            ct);
    }

    public async Task LockMemberAsync(long memberId, string? reason = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            return;
        }

        var beforeLockout = member.LockoutUntilUtc;
        var beforeCount = member.FailedLoginCount;
        member.LockoutUntilUtc = DateTime.UtcNow.AddYears(10);
        member.FailedLoginCount = MaxFailedLoginAttempts;
        member.LastFailedLoginReason = string.IsNullOrWhiteSpace(reason) ? "manual_lock" : reason.Trim();
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteChangeAsync(
            "member_lock",
            member.UserName,
            [
                new AuditChange("LockoutUntilUtc", beforeLockout?.ToString("o"), member.LockoutUntilUtc?.ToString("o")),
                new AuditChange("FailedLoginCount", beforeCount.ToString(CultureInfo.InvariantCulture), member.FailedLoginCount.ToString(CultureInfo.InvariantCulture))
            ],
            true,
            reason ?? "manual lock",
            ct);
    }

    public async Task UnlockMemberAsync(long memberId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            return;
        }

        var beforeLockout = member.LockoutUntilUtc;
        var beforeCount = member.FailedLoginCount;
        member.LockoutUntilUtc = null;
        member.FailedLoginCount = 0;
        member.LastFailedLoginUtc = null;
        member.LastFailedLoginReason = null;
        member.LastFailedLoginIp = null;
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteChangeAsync(
            "member_unlock",
            member.UserName,
            [
                new AuditChange("LockoutUntilUtc", beforeLockout?.ToString("o"), null),
                new AuditChange("FailedLoginCount", beforeCount.ToString(CultureInfo.InvariantCulture), "0")
            ],
            true,
            "manual unlock",
            ct);
    }

    public async Task ChangePasswordAsync(long memberId, string newPassword, CancellationToken ct = default)
    {
        await ChangePasswordAsyncInternal(memberId, newPassword, true, ct);
    }

    public async Task ChangeOwnPasswordAsync(string userName, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var member = await ValidatePasswordAsync(userName, currentPassword, ct);
        if (member is null)
        {
            await WriteAuditAsync("password_change_self", NormalizeUserName(userName), false, "current password invalid", ct);
            throw new InvalidOperationException("Current password is invalid.");
        }

        await ChangePasswordAsyncInternal(member.Id, newPassword, false, ct);
        await WriteAuditAsync("password_change_self", member.UserName, true, "self service password change", ct);
    }

    public async Task UpdateDisplayNameAsync(long memberId, string displayName, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            return;
        }

        var before = member.DisplayName;
        member.DisplayName = string.IsNullOrWhiteSpace(displayName) ? member.DisplayName : displayName.Trim();
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteChangeAsync(
            "member_display_name",
            member.UserName,
            [new AuditChange("DisplayName", before, member.DisplayName)],
            true,
            "Member display name updated",
            ct);
    }

    public async Task UpdateRoleAsync(long memberId, string role, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            return;
        }

        var normalizedRole = NormalizeRole(role);
        if (string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalizedRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var activeAdmins = await db.Members.CountAsync(x => x.IsActive && x.Role == "Admin", ct);
            if (activeAdmins <= 1)
            {
                throw new InvalidOperationException("At least one active admin must remain.");
            }
        }

        var before = member.Role;
        member.Role = normalizedRole;
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteChangeAsync(
            "member_role",
            member.UserName,
            [new AuditChange("Role", before, member.Role)],
            true,
            "Member role updated",
            ct);
    }

    public async Task UpdateLastLoginUtcAsync(long memberId, DateTime utcNow, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            return;
        }

        member.LastLoginUtc = utcNow;
        member.FailedLoginCount = 0;
        member.LockoutUntilUtc = null;
        member.LastFailedLoginReason = null;
        member.LastFailedLoginUtc = null;
        member.LastFailedLoginIp = null;
        await db.SaveChangesAsync(ct);
    }

    public string GenerateTwoFactorOtpUri(string userName, string secret, string issuer = "OpsEye")
    {
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(userName)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits={TwoFactorDigits}&period={TwoFactorStepSeconds}";
    }

    public string GenerateTwoFactorQrCodeDataUrl(string userName, string secret, string issuer = "OpsEye")
    {
        var otpUri = GenerateTwoFactorOtpUri(userName, secret, issuer);
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(otpUri, QRCodeGenerator.ECCLevel.Q);
        var svgQrCode = new SvgQRCode(qrData);
        var svg = svgQrCode.GetGraphic(6);
        var bytes = Encoding.UTF8.GetBytes(svg);
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}";
    }

    public async Task<List<MemberLoginAttemptEntity>> GetRecentLoginAttemptsAsync(string? userName = null, int take = 20, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.MemberLoginAttempts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(userName))
        {
            var normalized = NormalizeUserName(userName);
            query = query.Where(x => x.UserName == normalized);
        }

        var limit = take <= 0 ? 20 : Math.Min(take, 100);
        return await query
            .OrderByDescending(x => x.CreatedUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public static string NormalizeUserName(string value) => value.Trim().ToLowerInvariant();

    public static string NormalizeRole(string? value)
    {
        var role = string.IsNullOrWhiteSpace(value) ? "User" : value.Trim();
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "User";
    }

    public static string? ValidatePasswordPolicy(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "Password is required.";
        }

        if (password.Length < 12)
        {
            return "Password must be at least 12 characters long.";
        }

        if (password.Any(char.IsWhiteSpace))
        {
            return "Password cannot contain whitespace.";
        }

        if (!password.Any(char.IsUpper))
        {
            return "Password must include at least one uppercase letter.";
        }

        if (!password.Any(char.IsLower))
        {
            return "Password must include at least one lowercase letter.";
        }

        if (!password.Any(char.IsDigit))
        {
            return "Password must include at least one number.";
        }

        if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            return "Password must include at least one special character.";
        }

        var normalizedUserName = NormalizeUserName(userName);
        if (!string.IsNullOrWhiteSpace(normalizedUserName) &&
            password.Contains(normalizedUserName, StringComparison.OrdinalIgnoreCase))
        {
            return "Password cannot contain the user name.";
        }

        return null;
    }

    private async Task<MemberEntity> CreateMemberCoreAsync(
        string userName,
        string displayName,
        string password,
        string role,
        string? emailAddress,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var normalizedUserName = NormalizeUserName(userName);
        var exists = await db.Members.AnyAsync(x => x.UserName == normalizedUserName, ct);
        if (exists)
        {
            throw new InvalidOperationException("User name already exists.");
        }

        var passwordError = ValidatePasswordPolicy(normalizedUserName, password);
        if (passwordError is not null)
        {
            throw new InvalidOperationException(passwordError);
        }

        var normalizedEmail = NormalizeEmailAddress(emailAddress);

        var member = new MemberEntity
        {
            UserName = normalizedUserName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUserName : displayName.Trim(),
            EmailAddress = normalizedEmail,
            EmailConfirmedUtc = null,
            Role = NormalizeRole(role),
            CreatedUtc = DateTime.UtcNow,
            IsActive = true,
            PasswordChangedUtc = DateTime.UtcNow
        };
        member.PasswordHash = passwordHasher.HashPassword(member, password);

        db.Members.Add(member);
        await db.SaveChangesAsync(ct);
        return member;
    }

    private async Task ChangePasswordAsyncInternal(long memberId, string newPassword, bool writeAudit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null)
        {
            throw new InvalidOperationException("Member not found.");
        }

        var passwordError = ValidatePasswordPolicy(member.UserName, newPassword);
        if (passwordError is not null)
        {
            throw new InvalidOperationException(passwordError);
        }

        var before = member.PasswordChangedUtc;
        member.PasswordHash = passwordHasher.HashPassword(member, newPassword);
        member.PasswordChangedUtc = DateTime.UtcNow;
        member.MustChangePassword = false;
        await db.SaveChangesAsync(ct);

        if (writeAudit)
        {
            await auditLogService.WriteChangeAsync(
                "password_change",
                member.UserName,
                [new AuditChange("PasswordChangedUtc", before?.ToString("o"), member.PasswordChangedUtc?.ToString("o"))],
                true,
                "Password updated",
                ct);
        }
    }

    private async Task WriteAuditAsync(string action, string? targetUserName, bool success, string? details, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.MemberAuditLogs.Add(new MemberAuditLogEntity
            {
                ActorUserName = ResolveActorUserName(),
                TargetUserName = string.IsNullOrWhiteSpace(targetUserName) ? null : targetUserName.Trim(),
                Action = action,
                Details = details,
                Success = success,
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Audit logs should never block the core operation.
        }
    }

    private string? ResolveRequestIpAddress()
    {
        return httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }

    private string? ResolveRequestUserAgent()
    {
        return httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
    }

    private static async Task WriteLoginAttemptAsync(
        MonitoringDbContext db,
        string userName,
        long? memberId,
        bool success,
        string reason,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        db.MemberLoginAttempts.Add(new MemberLoginAttemptEntity
        {
            UserName = NormalizeUserName(userName),
            MemberId = memberId,
            Success = success,
            Reason = reason,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim(),
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private string ResolveActorUserName()
    {
        var userName = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(userName) ? "system" : NormalizeUserName(userName);
    }

    private static string GenerateSessionId()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateResetToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private EmailSettings GetEmailSettings()
    {
        var baseSettings = configuration.GetSection("Monitoring:Email").Get<EmailSettings>() ?? new EmailSettings();
        return emailSettingsRepository.Load(baseSettings);
    }

    private static string GenerateTwoFactorSecret()
    {
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    private string ProtectTwoFactorSecret(string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = _twoFactorProtector.Protect(bytes);
        return Convert.ToBase64String(protectedBytes);
    }

    private string UnprotectTwoFactorSecret(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(storedValue);
            var bytes = _twoFactorProtector.Unprotect(protectedBytes);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return storedValue;
        }
    }

    private static string GenerateRecoveryCode()
    {
        Span<byte> bytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(bytes);
        var raw = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{raw[..4]}-{raw[4..8]}-{raw[8..12]}";
    }

    private static string NormalizeRecoveryCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var cleaned = new string(code.Where(char.IsLetterOrDigit).ToArray());
        return cleaned.ToUpperInvariant();
    }

    private static string NormalizeEmailAddress(string? emailAddress)
    {
        return string.IsNullOrWhiteSpace(emailAddress) ? string.Empty : emailAddress.Trim();
    }

    private static string NormalizeVerificationToken(string? token)
    {
        return string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool VerifyTwoFactorCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalizedCode = new string(code.Where(char.IsDigit).ToArray());
        if (normalizedCode.Length != TwoFactorDigits)
        {
            return false;
        }

        var secretBytes = Base32Decode(secret);
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / TwoFactorStepSeconds;
        for (var offset = -1; offset <= 1; offset++)
        {
            if (GenerateTotp(secretBytes, timeStep + offset) == normalizedCode)
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateTotp(byte[] key, long timestep)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(timestep & 0xFF);
            timestep >>= 8;
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];

        var otp = binary % (int)Math.Pow(10, TwoFactorDigits);
        return otp.ToString(new string('0', TwoFactorDigits), CultureInfo.InvariantCulture);
    }

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder((data.Length + 4) / 5 * 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            output.Append(alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return output.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var normalized = value.Trim().Replace("=", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        var bytes = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var ch in normalized)
        {
            var index = alphabet.IndexOf(ch);
            if (index < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return bytes.ToArray();
    }
}
