namespace Monitoring.Blazor.Services;

internal static class SqlScripts
{
    internal const string EnsureAlertAcknowledgementColumns = """
IF COL_LENGTH(N'[dbo].[AlertEvents]', N'AcknowledgedUtc') IS NULL
BEGIN
    ALTER TABLE [dbo].[AlertEvents] ADD [AcknowledgedUtc] datetime2 NULL;
END;
IF COL_LENGTH(N'[dbo].[AlertEvents]', N'AcknowledgedBy') IS NULL
BEGIN
    ALTER TABLE [dbo].[AlertEvents] ADD [AcknowledgedBy] nvarchar(100) NULL;
END;
""";
    internal const string EnsureAlertSuppressionsTable = """
IF OBJECT_ID(N'[dbo].[AlertSuppressions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AlertSuppressions] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [Hostname] nvarchar(200) NOT NULL,
        [Metric] nvarchar(200) NOT NULL,
        [UntilUtc] datetime2 NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Kind] nvarchar(50) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AlertSuppressions] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AlertSuppressions_Hostname_Metric' AND object_id = OBJECT_ID(N'[dbo].[AlertSuppressions]'))
BEGIN
    CREATE UNIQUE INDEX [IX_AlertSuppressions_Hostname_Metric] ON [dbo].[AlertSuppressions] ([Hostname], [Metric]);
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AlertSuppressions_UntilUtc' AND object_id = OBJECT_ID(N'[dbo].[AlertSuppressions]'))
BEGIN
    CREATE INDEX [IX_AlertSuppressions_UntilUtc] ON [dbo].[AlertSuppressions] ([UntilUtc]);
END;
""";

    internal const string EnsureLogIpDailyStatsTable = """
IF OBJECT_ID(N'[dbo].[LogIpDailyStats]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LogIpDailyStats] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [ServerName] nvarchar(200) NOT NULL,
        [LogDate] date NOT NULL,
        [Ip] nvarchar(200) NOT NULL,
        [RequestCount] bigint NOT NULL,
        [Status2xxCount] bigint NOT NULL,
        [Status3xxCount] bigint NOT NULL,
        [Status4xxCount] bigint NOT NULL,
        [Status5xxCount] bigint NOT NULL,
        [FirstSeenUtc] datetime2 NOT NULL,
        [LastSeenUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_LogIpDailyStats] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LogIpDailyStats_ServerName_LogDate_Ip'
      AND object_id = OBJECT_ID(N'[dbo].[LogIpDailyStats]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_LogIpDailyStats_ServerName_LogDate_Ip]
        ON [dbo].[LogIpDailyStats] ([ServerName], [LogDate], [Ip]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LogIpDailyStats_ServerName_LogDate_RequestCount'
      AND object_id = OBJECT_ID(N'[dbo].[LogIpDailyStats]')
)
BEGIN
    CREATE INDEX [IX_LogIpDailyStats_ServerName_LogDate_RequestCount]
        ON [dbo].[LogIpDailyStats] ([ServerName], [LogDate], [RequestCount]);
END;
""";

    internal const string EnsureMembersTable = """
IF OBJECT_ID(N'[dbo].[Members]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Members] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [UserName] nvarchar(100) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [EmailAddress] nvarchar(320) NULL,
        [EmailConfirmedUtc] datetime2 NULL,
        [Role] nvarchar(50) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [LastLoginUtc] datetime2 NULL,
        [FailedLoginCount] int NOT NULL CONSTRAINT [DF_Members_FailedLoginCount] DEFAULT(0),
        [LastFailedLoginUtc] datetime2 NULL,
        [LockoutUntilUtc] datetime2 NULL,
        [LastFailedLoginReason] nvarchar(200) NULL,
        [LastFailedLoginIp] nvarchar(100) NULL,
        [PasswordChangedUtc] datetime2 NULL,
        [MustChangePassword] bit NOT NULL CONSTRAINT [DF_Members_MustChangePassword] DEFAULT(0),
        [TwoFactorEnabled] bit NOT NULL CONSTRAINT [DF_Members_TwoFactorEnabled] DEFAULT(0),
        [TwoFactorSecret] nvarchar(200) NULL,
        [TwoFactorEnabledUtc] datetime2 NULL,
        CONSTRAINT [PK_Members] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_Members_UserName'
      AND object_id = OBJECT_ID(N'[dbo].[Members]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_Members_UserName]
        ON [dbo].[Members] ([UserName]);
END;

IF COL_LENGTH(N'[dbo].[Members]', N'FailedLoginCount') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [FailedLoginCount] int NOT NULL CONSTRAINT [DF_Members_FailedLoginCount] DEFAULT(0);
END;

IF COL_LENGTH(N'[dbo].[Members]', N'LastFailedLoginUtc') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [LastFailedLoginUtc] datetime2 NULL;
END;

IF COL_LENGTH(N'[dbo].[Members]', N'LockoutUntilUtc') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [LockoutUntilUtc] datetime2 NULL;
END;

IF COL_LENGTH(N'[dbo].[Members]', N'LastFailedLoginReason') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [LastFailedLoginReason] nvarchar(200) NULL;
END;

IF COL_LENGTH(N'[dbo].[Members]', N'LastFailedLoginIp') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [LastFailedLoginIp] nvarchar(100) NULL;
END;

IF COL_LENGTH(N'[dbo].[Members]', N'EmailAddress') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [EmailAddress] nvarchar(320) NULL;
END;

IF COL_LENGTH(N'[dbo].[Members]', N'EmailConfirmedUtc') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [EmailConfirmedUtc] datetime2 NULL;
END;

IF COL_LENGTH(N'[dbo].[Members]', N'PasswordChangedUtc') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [PasswordChangedUtc] datetime2 NULL;
END;

IF COL_LENGTH(N'[dbo].[Members]', N'MustChangePassword') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [MustChangePassword] bit NOT NULL CONSTRAINT [DF_Members_MustChangePassword] DEFAULT(0);
END;

IF COL_LENGTH(N'[dbo].[Members]', N'TwoFactorEnabled') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [TwoFactorEnabled] bit NOT NULL CONSTRAINT [DF_Members_TwoFactorEnabled] DEFAULT(0);
END;

IF COL_LENGTH(N'[dbo].[Members]', N'TwoFactorSecret') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [TwoFactorSecret] nvarchar(200) NULL;
END;

IF COL_LENGTH(N'[dbo].[Members]', N'TwoFactorEnabledUtc') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [TwoFactorEnabledUtc] datetime2 NULL;
END;
""";

    internal const string EnsureMemberAuditLogsTable = """
IF OBJECT_ID(N'[dbo].[MemberAuditLogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MemberAuditLogs] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [ActorUserName] nvarchar(100) NOT NULL,
        [TargetUserName] nvarchar(100) NULL,
        [Action] nvarchar(100) NOT NULL,
        [Details] nvarchar(2000) NULL,
        [Success] bit NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_MemberAuditLogs] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_MemberAuditLogs_CreatedUtc_Action'
      AND object_id = OBJECT_ID(N'[dbo].[MemberAuditLogs]')
)
BEGIN
    CREATE INDEX [IX_MemberAuditLogs_CreatedUtc_Action]
        ON [dbo].[MemberAuditLogs] ([CreatedUtc], [Action]);
END;
""";

    internal const string EnsureMemberLoginAttemptsTable = """
IF OBJECT_ID(N'[dbo].[MemberLoginAttempts]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MemberLoginAttempts] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [UserName] nvarchar(100) NOT NULL,
        [MemberId] bigint NULL,
        [Success] bit NOT NULL,
        [Reason] nvarchar(200) NOT NULL,
        [IpAddress] nvarchar(100) NULL,
        [UserAgent] nvarchar(500) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_MemberLoginAttempts] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_MemberLoginAttempts_CreatedUtc_UserName'
      AND object_id = OBJECT_ID(N'[dbo].[MemberLoginAttempts]')
)
BEGIN
    CREATE INDEX [IX_MemberLoginAttempts_CreatedUtc_UserName]
        ON [dbo].[MemberLoginAttempts] ([CreatedUtc], [UserName]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_MemberLoginAttempts_UserName_CreatedUtc'
      AND object_id = OBJECT_ID(N'[dbo].[MemberLoginAttempts]')
)
BEGIN
    CREATE INDEX [IX_MemberLoginAttempts_UserName_CreatedUtc]
        ON [dbo].[MemberLoginAttempts] ([UserName], [CreatedUtc]);
END;
""";

    internal const string EnsureMemberSessionsTable = """
IF OBJECT_ID(N'[dbo].[MemberSessions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MemberSessions] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [SessionId] nvarchar(100) NOT NULL,
        [MemberId] bigint NOT NULL,
        [IpAddress] nvarchar(100) NULL,
        [UserAgent] nvarchar(500) NULL,
        [IsPersistent] bit NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [LastSeenUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [RevokedUtc] datetime2 NULL,
        [RevokeReason] nvarchar(200) NULL,
        CONSTRAINT [PK_MemberSessions] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_MemberSessions_SessionId'
      AND object_id = OBJECT_ID(N'[dbo].[MemberSessions]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_MemberSessions_SessionId]
        ON [dbo].[MemberSessions] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_MemberSessions_MemberId_ExpiresUtc'
      AND object_id = OBJECT_ID(N'[dbo].[MemberSessions]')
)
BEGIN
    CREATE INDEX [IX_MemberSessions_MemberId_ExpiresUtc]
        ON [dbo].[MemberSessions] ([MemberId], [ExpiresUtc]);
END;
""";

    internal const string EnsurePasswordResetRequestsTable = """
IF OBJECT_ID(N'[dbo].[PasswordResetRequests]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PasswordResetRequests] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [UserName] nvarchar(100) NOT NULL,
        [TokenHash] nvarchar(128) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [UsedUtc] datetime2 NULL,
        [RequestedIp] nvarchar(100) NULL,
        [RequestedUserAgent] nvarchar(500) NULL,
        CONSTRAINT [PK_PasswordResetRequests] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_PasswordResetRequests_TokenHash'
      AND object_id = OBJECT_ID(N'[dbo].[PasswordResetRequests]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_PasswordResetRequests_TokenHash]
        ON [dbo].[PasswordResetRequests] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_PasswordResetRequests_UserName_CreatedUtc'
      AND object_id = OBJECT_ID(N'[dbo].[PasswordResetRequests]')
)
BEGIN
    CREATE INDEX [IX_PasswordResetRequests_UserName_CreatedUtc]
        ON [dbo].[PasswordResetRequests] ([UserName], [CreatedUtc]);
END;
""";

    internal const string EnsureMemberRecoveryCodesTable = """
IF OBJECT_ID(N'[dbo].[MemberRecoveryCodes]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[MemberRecoveryCodes] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [MemberId] bigint NOT NULL,
        [CodeHash] nvarchar(128) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UsedUtc] datetime2 NULL,
        CONSTRAINT [PK_MemberRecoveryCodes] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_MemberRecoveryCodes_MemberId_UsedUtc'
      AND object_id = OBJECT_ID(N'[dbo].[MemberRecoveryCodes]')
)
BEGIN
    CREATE INDEX [IX_MemberRecoveryCodes_MemberId_UsedUtc]
        ON [dbo].[MemberRecoveryCodes] ([MemberId], [UsedUtc]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_MemberRecoveryCodes_CodeHash'
      AND object_id = OBJECT_ID(N'[dbo].[MemberRecoveryCodes]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_MemberRecoveryCodes_CodeHash]
        ON [dbo].[MemberRecoveryCodes] ([CodeHash]);
END;
""";

    internal const string EnsureEmailVerificationRequestsTable = """
IF OBJECT_ID(N'[dbo].[EmailVerificationRequests]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmailVerificationRequests] (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [MemberId] bigint NOT NULL,
        [EmailAddress] nvarchar(320) NOT NULL,
        [TokenHash] nvarchar(128) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [UsedUtc] datetime2 NULL,
        CONSTRAINT [PK_EmailVerificationRequests] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_EmailVerificationRequests_TokenHash'
      AND object_id = OBJECT_ID(N'[dbo].[EmailVerificationRequests]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_EmailVerificationRequests_TokenHash]
        ON [dbo].[EmailVerificationRequests] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_EmailVerificationRequests_MemberId_UsedUtc'
      AND object_id = OBJECT_ID(N'[dbo].[EmailVerificationRequests]')
)
BEGIN
    CREATE INDEX [IX_EmailVerificationRequests_MemberId_UsedUtc]
        ON [dbo].[EmailVerificationRequests] ([MemberId], [UsedUtc]);
END;
""";
}
