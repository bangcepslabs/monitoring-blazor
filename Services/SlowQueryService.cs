using Microsoft.Data.SqlClient;

namespace Monitoring.Blazor.Services;

public sealed class SlowQueryService(
    IConfiguration configuration,
    ILogger<SlowQueryService> logger,
    SqlTargetSettingsRepository sqlTargetSettingsRepository,
    AuditLogService auditLogService)
{
    public async Task<SlowQueryResult> GetSlowQueriesAsync(string hostname, CancellationToken ct)
    {
        var connString = ResolveConnectionString(hostname);
        if (string.IsNullOrWhiteSpace(connString))
        {
            return SlowQueryResult.Fail($"No SQL connection string configured for host '{hostname}'.");
        }

        var thresholdSeconds = configuration.GetValue("Monitoring:SlowQuery:ThresholdSeconds", 5);
        var minMs = Math.Max(1, thresholdSeconds) * 1000;

        const string sql = @"
SELECT TOP (50)
    r.session_id,
    r.status,
    r.command,
    r.cpu_time,
    r.total_elapsed_time,
    r.reads,
    r.writes,
    r.logical_reads,
    r.wait_type,
    r.wait_time,
    r.blocking_session_id,
    DB_NAME(r.database_id) AS database_name,
    SUBSTRING(t.text,
        (r.statement_start_offset / 2) + 1,
        (CASE r.statement_end_offset
            WHEN -1 THEN LEN(t.text)
            ELSE (r.statement_end_offset - r.statement_start_offset) / 2 + 1
         END)
    ) AS statement_text,
    t.text AS batch_text
FROM sys.dm_exec_requests r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.total_elapsed_time >= @minMs
  AND r.session_id <> @@SPID
ORDER BY r.total_elapsed_time DESC;";

        try
        {
            await using var conn = new SqlConnection(connString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@minMs", minMs);

            var rows = new List<SlowQueryRow>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new SlowQueryRow
                {
                    SessionId = reader.GetInt16(0),
                    Status = reader.GetString(1),
                    Command = reader.GetString(2),
                    CpuTimeMs = reader.GetInt32(3),
                    ElapsedMs = reader.GetInt32(4),
                    Reads = reader.GetInt64(5),
                    Writes = reader.GetInt64(6),
                    LogicalReads = reader.GetInt64(7),
                    WaitType = reader.IsDBNull(8) ? null : reader.GetString(8),
                    WaitTimeMs = reader.GetInt32(9),
                    BlockingSessionId = reader.IsDBNull(10) ? null : reader.GetInt16(10),
                    DatabaseName = reader.IsDBNull(11) ? null : reader.GetString(11),
                    StatementText = reader.IsDBNull(12) ? null : reader.GetString(12),
                    BatchText = reader.IsDBNull(13) ? null : reader.GetString(13)
                });
            }

            return SlowQueryResult.Ok(rows);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch slow queries for {Host}", hostname);
            return SlowQueryResult.Fail(BuildFriendlyError(ex, "slow queries"));
        }
    }

    private string? ResolveConnectionString(string hostname)
    {
        var saved = sqlTargetSettingsRepository.Get(hostname, GetFallbackSettings(hostname));
        var savedConnectionString = saved.BuildConnectionString();
        if (!string.IsNullOrWhiteSpace(savedConnectionString))
        {
            return savedConnectionString;
        }

        var section = configuration.GetSection("Monitoring:SqlTargets");
        if (section.Exists())
        {
            var byHost = section[hostname];
            if (!string.IsNullOrWhiteSpace(byHost))
            {
                return byHost;
            }

            var lower = hostname.ToLowerInvariant();
            foreach (var child in section.GetChildren())
            {
                if (string.Equals(child.Key, lower, StringComparison.OrdinalIgnoreCase))
                {
                    return child.Value;
                }
            }
        }

        return configuration.GetConnectionString("MonitoringDb");
    }

    public SqlTargetSettings GetSqlTargetSettings(string hostname)
    {
        return sqlTargetSettingsRepository.Get(hostname, GetFallbackSettings(hostname));
    }

    public void SaveSqlTargetSettings(string hostname, SqlTargetSettings settings)
    {
        var before = sqlTargetSettingsRepository.LoadAll().TryGetValue(hostname, out var existing) ? existing : null;
        sqlTargetSettingsRepository.Save(hostname, settings);
        _ = auditLogService.WriteChangeAsync(
            "sql_target_update",
            hostname,
            [
                new AuditChange("Server", before?.Server, settings.Server),
                new AuditChange("Port", before?.Port.ToString(), settings.Port.ToString()),
                new AuditChange("Database", before?.Database, settings.Database),
                new AuditChange("Username", before?.Username, settings.Username),
                new AuditChange("IntegratedSecurity", before?.IntegratedSecurity.ToString(), settings.IntegratedSecurity.ToString()),
                new AuditChange("Encrypt", before?.Encrypt.ToString(), settings.Encrypt.ToString()),
                new AuditChange("TrustServerCertificate", before?.TrustServerCertificate.ToString(), settings.TrustServerCertificate.ToString())
            ],
            true,
            "SQL access settings updated",
            CancellationToken.None);
    }

    public void RemoveSqlTargetSettings(string hostname)
    {
        var before = sqlTargetSettingsRepository.LoadAll().TryGetValue(hostname, out var existing) ? existing : null;
        sqlTargetSettingsRepository.Remove(hostname);
        _ = auditLogService.WriteChangeAsync(
            "sql_target_remove",
            hostname,
            [
                new AuditChange("Server", before?.Server, null),
                new AuditChange("Database", before?.Database, null),
                new AuditChange("Username", before?.Username, null)
            ],
            true,
            "SQL access settings removed",
            CancellationToken.None);
    }

    private SqlTargetSettings GetFallbackSettings(string hostname)
    {
        var section = configuration.GetSection("Monitoring:SqlTargets");
        var byHost = section[hostname];
        if (!string.IsNullOrWhiteSpace(byHost))
        {
            return SqlTargetSettings.FromConnectionString(byHost);
        }

        var lower = hostname.ToLowerInvariant();
        foreach (var child in section.GetChildren())
        {
            if (string.Equals(child.Key, lower, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(child.Value))
            {
                return SqlTargetSettings.FromConnectionString(child.Value);
            }
        }

        var monitoringDb = configuration.GetConnectionString("MonitoringDb");
        return string.IsNullOrWhiteSpace(monitoringDb)
            ? new SqlTargetSettings()
            : SqlTargetSettings.FromConnectionString(monitoringDb);
    }

    private static string BuildFriendlyError(Exception ex, string queryType)
    {
        var message = ex.Message;
        if (message.Contains("VIEW SERVER PERFORMANCE STATE", StringComparison.OrdinalIgnoreCase))
        {
            return $"SQL permission denied while loading {queryType}. Grant VIEW SERVER PERFORMANCE STATE on SQL Server 2022+, or VIEW SERVER STATE on older versions.";
        }

        if (message.Contains("VIEW SERVER STATE", StringComparison.OrdinalIgnoreCase))
        {
            return $"SQL permission denied while loading {queryType}. Grant VIEW SERVER STATE on the SQL Server login.";
        }

        return message;
    }

    public async Task<BlockingQueryResult> GetBlockingQueriesAsync(string hostname, CancellationToken ct)
    {
        var connString = ResolveConnectionString(hostname);
        if (string.IsNullOrWhiteSpace(connString))
        {
            return BlockingQueryResult.Fail($"No SQL connection string configured for host '{hostname}'.");
        }

        const string sql = @"
SELECT TOP (50)
    r.session_id,
    r.status,
    r.command,
    r.cpu_time,
    r.total_elapsed_time,
    r.wait_type,
    r.wait_time,
    r.blocking_session_id,
    DB_NAME(r.database_id) AS database_name,
    SUBSTRING(t.text,
        (r.statement_start_offset / 2) + 1,
        (CASE r.statement_end_offset
            WHEN -1 THEN LEN(t.text)
            ELSE (r.statement_end_offset - r.statement_start_offset) / 2 + 1
         END)
    ) AS statement_text
FROM sys.dm_exec_requests r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.blocking_session_id <> 0
ORDER BY r.total_elapsed_time DESC;";

        try
        {
            await using var conn = new SqlConnection(connString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);

            var rows = new List<BlockingQueryRow>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new BlockingQueryRow
                {
                    SessionId = reader.GetInt16(0),
                    Status = reader.GetString(1),
                    Command = reader.GetString(2),
                    CpuTimeMs = reader.GetInt32(3),
                    ElapsedMs = reader.GetInt32(4),
                    WaitType = reader.IsDBNull(5) ? null : reader.GetString(5),
                    WaitTimeMs = reader.GetInt32(6),
                    BlockingSessionId = reader.GetInt16(7),
                    DatabaseName = reader.IsDBNull(8) ? null : reader.GetString(8),
                    StatementText = reader.IsDBNull(9) ? null : reader.GetString(9)
                });
            }

            return BlockingQueryResult.Ok(rows);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch blocking queries for {Host}", hostname);
            return BlockingQueryResult.Fail(BuildFriendlyError(ex, "blocking sessions"));
        }
    }

    public async Task<TopIoQueryResult> GetTopIoQueriesAsync(string hostname, CancellationToken ct)
    {
        var connString = ResolveConnectionString(hostname);
        if (string.IsNullOrWhiteSpace(connString))
        {
            return TopIoQueryResult.Fail($"No SQL connection string configured for host '{hostname}'.");
        }

        const string sql = @"
SELECT TOP (50)
    qs.total_logical_reads,
    qs.total_logical_writes,
    qs.total_worker_time,
    qs.total_elapsed_time,
    qs.execution_count,
    DB_NAME(st.dbid) AS database_name,
    SUBSTRING(st.text,
        (qs.statement_start_offset / 2) + 1,
        (CASE qs.statement_end_offset
            WHEN -1 THEN LEN(st.text)
            ELSE (qs.statement_end_offset - qs.statement_start_offset) / 2 + 1
         END)
    ) AS statement_text
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
ORDER BY qs.total_logical_reads DESC;";

        try
        {
            await using var conn = new SqlConnection(connString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);

            var rows = new List<TopIoQueryRow>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new TopIoQueryRow
                {
                    TotalLogicalReads = reader.GetInt64(0),
                    TotalLogicalWrites = reader.GetInt64(1),
                    TotalCpuTimeMs = reader.GetInt64(2),
                    TotalElapsedMs = reader.GetInt64(3),
                    ExecutionCount = reader.GetInt64(4),
                    DatabaseName = reader.IsDBNull(5) ? null : reader.GetString(5),
                    StatementText = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }

            return TopIoQueryResult.Ok(rows);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch top IO queries for {Host}", hostname);
            return TopIoQueryResult.Fail(BuildFriendlyError(ex, "top IO queries"));
        }
    }
}

public sealed class SlowQueryRow
{
    public short SessionId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public int CpuTimeMs { get; init; }
    public int ElapsedMs { get; init; }
    public long Reads { get; init; }
    public long Writes { get; init; }
    public long LogicalReads { get; init; }
    public string? WaitType { get; init; }
    public int WaitTimeMs { get; init; }
    public short? BlockingSessionId { get; init; }
    public string? DatabaseName { get; init; }
    public string? StatementText { get; init; }
    public string? BatchText { get; init; }
}

public sealed record SlowQueryResult(bool Success, string? Error, List<SlowQueryRow> Rows)
{
    public static SlowQueryResult Ok(List<SlowQueryRow> rows) => new(true, null, rows);
    public static SlowQueryResult Fail(string error) => new(false, error, []);
}

public sealed class BlockingQueryRow
{
    public short SessionId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public int CpuTimeMs { get; init; }
    public int ElapsedMs { get; init; }
    public string? WaitType { get; init; }
    public int WaitTimeMs { get; init; }
    public short BlockingSessionId { get; init; }
    public string? DatabaseName { get; init; }
    public string? StatementText { get; init; }
}

public sealed record BlockingQueryResult(bool Success, string? Error, List<BlockingQueryRow> Rows)
{
    public static BlockingQueryResult Ok(List<BlockingQueryRow> rows) => new(true, null, rows);
    public static BlockingQueryResult Fail(string error) => new(false, error, []);
}

public sealed class TopIoQueryRow
{
    public long TotalLogicalReads { get; init; }
    public long TotalLogicalWrites { get; init; }
    public long TotalCpuTimeMs { get; init; }
    public long TotalElapsedMs { get; init; }
    public long ExecutionCount { get; init; }
    public string? DatabaseName { get; init; }
    public string? StatementText { get; init; }
}

public sealed record TopIoQueryResult(bool Success, string? Error, List<TopIoQueryRow> Rows)
{
    public static TopIoQueryResult Ok(List<TopIoQueryRow> rows) => new(true, null, rows);
    public static TopIoQueryResult Fail(string error) => new(false, error, []);
}
