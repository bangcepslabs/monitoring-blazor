using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Monitoring.Blazor.Models;
using System.Text;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Monitoring.Blazor.Services;

public sealed record IisLogWorkspaceSummary(
    long TotalRows,
    long UniqueIps,
    long UniqueUrls,
    List<KeyValuePair<string, long>> StatusCounts,
    List<KeyValuePair<string, long>> TopUrls,
    List<KeyValuePair<string, long>> TopIps);

/// <summary>
/// Disk-backed, short-lived storage for parsed IIS rows. Raw rows never enter the main monitoring DB.
/// </summary>
public sealed class IisLogWorkspaceStore
{
    private readonly string _root;
    private static readonly TimeSpan WorkspaceLifetime = TimeSpan.FromHours(24);

    public IisLogWorkspaceStore(IConfiguration configuration)
    {
        var dataDirectory = configuration.GetValue<string>("Monitoring:DataDirectory");
        _root = Path.Combine(
            string.IsNullOrWhiteSpace(dataDirectory) ? AppContext.BaseDirectory : dataDirectory,
            "iis-log-workspaces");
        Directory.CreateDirectory(_root);
    }

    public bool Exists(string? workspaceId) =>
        Guid.TryParseExact(workspaceId, "N", out _) && File.Exists(GetPath(workspaceId!));

    public async Task<WorkspaceWriter> CreateAsync(long sourceBytes, DateTime fallbackUtc, CancellationToken cancellationToken)
    {
        await CleanupExpiredAsync(cancellationToken);
        EnsureAvailableSpace(sourceBytes);

        var id = Guid.NewGuid().ToString("N");
        var path = GetPath(id);
        var connection = await OpenConnectionAsync(path, cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                CREATE TABLE LogRows (
                    Id INTEGER PRIMARY KEY,
                    Date TEXT NOT NULL,
                    Time TEXT NOT NULL,
                    Ip TEXT NOT NULL,
                    Method TEXT NOT NULL,
                    Uri TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    Referrer TEXT NOT NULL,
                    UserAgent TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        return new WorkspaceWriter(id, path, connection, DateOnly.FromDateTime(fallbackUtc));
    }

    public async Task<long> GetCountAsync(string workspaceId, IisLogFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(GetPath(workspaceId), cancellationToken);
        await EnsureDefaultPageIndexAsync(connection, cancellationToken);
        var query = BuildWhere(criteria);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM LogRows {query.Where};";
        AddParameters(command, query.Parameters);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task<List<ParsedLogRow>> GetPageAsync(
        string workspaceId,
        IisLogFilterCriteria criteria,
        IReadOnlyList<LogRowSort> sorts,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(GetPath(workspaceId), cancellationToken);
        var query = BuildWhere(criteria);
        var orderBy = BuildOrderBy(sorts);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Date, Time, Ip, Method, Uri, Status, Referrer, UserAgent
            FROM LogRows {query.Where}
            ORDER BY {orderBy}
            LIMIT $limit OFFSET $offset;
            """;
        AddParameters(command, query.Parameters);
        command.Parameters.AddWithValue("$limit", Math.Clamp(pageSize, 1, 500));
        command.Parameters.AddWithValue("$offset", (long)Math.Max(0, page - 1) * Math.Clamp(pageSize, 1, 500));

        var rows = new List<ParsedLogRow>(Math.Clamp(pageSize, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ParsedLogRow
            {
                Date = reader.GetString(0),
                Time = reader.GetString(1),
                Ip = reader.GetString(2),
                Method = reader.GetString(3),
                Uri = reader.GetString(4),
                Status = reader.GetString(5),
                Referrer = reader.GetString(6),
                UserAgent = reader.GetString(7)
            });
        }

        return rows;
    }

    public async Task<IisLogWorkspaceSummary> GetSummaryAsync(
        string workspaceId,
        IisLogFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(GetPath(workspaceId), cancellationToken);
        var query = BuildWhere(criteria);
        await using var totalsCommand = connection.CreateCommand();
        totalsCommand.CommandText = $"SELECT COUNT(*), COUNT(DISTINCT NULLIF(Ip, '')), COUNT(DISTINCT NULLIF(Uri, '')) FROM LogRows {query.Where};";
        AddParameters(totalsCommand, query.Parameters);
        await using var totalsReader = await totalsCommand.ExecuteReaderAsync(cancellationToken);
        await totalsReader.ReadAsync(cancellationToken);
        var totalRows = totalsReader.GetInt64(0);
        var uniqueIps = totalsReader.GetInt64(1);
        var uniqueUrls = totalsReader.GetInt64(2);
        var statusCounts = await GetGroupedCountsAsync(connection, query, "Status", 0, cancellationToken);
        var topUrls = await GetGroupedCountsAsync(connection, query, "Uri", 8, cancellationToken);
        var topIps = await GetGroupedCountsAsync(connection, query, "Ip", 5, cancellationToken);
        return new IisLogWorkspaceSummary(totalRows, uniqueIps, uniqueUrls, statusCounts, topUrls, topIps);
    }

    public async Task<long> GetAggregateGroupCountAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(GetPath(workspaceId), cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM (SELECT Date, Ip FROM LogRows GROUP BY Date, Ip);";
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async IAsyncEnumerable<List<LogIpAggregate>> ReadAggregateBatchesAsync(
        string workspaceId,
        int batchSize = 500,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(GetPath(workspaceId), cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Date AS LogDate,
                   Ip,
                   COUNT(*) AS RequestCount,
                   SUM(CASE WHEN trim(Status) <> '' AND trim(Status) NOT GLOB '*[^0-9]*' AND CAST(Status AS INTEGER) BETWEEN 200 AND 299 THEN 1 ELSE 0 END) AS Status2xxCount,
                   SUM(CASE WHEN trim(Status) <> '' AND trim(Status) NOT GLOB '*[^0-9]*' AND CAST(Status AS INTEGER) BETWEEN 300 AND 399 THEN 1 ELSE 0 END) AS Status3xxCount,
                   SUM(CASE WHEN trim(Status) <> '' AND trim(Status) NOT GLOB '*[^0-9]*' AND CAST(Status AS INTEGER) BETWEEN 400 AND 499 THEN 1 ELSE 0 END) AS Status4xxCount,
                   SUM(CASE WHEN trim(Status) <> '' AND trim(Status) NOT GLOB '*[^0-9]*' AND CAST(Status AS INTEGER) BETWEEN 500 AND 599 THEN 1 ELSE 0 END) AS Status5xxCount
            FROM LogRows
            GROUP BY Date, Ip
            ORDER BY LogDate, Ip;
            """;
        var boundedBatchSize = Math.Clamp(batchSize, 1, 2_000);
        var batch = new List<LogIpAggregate>(boundedBatchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsedDate = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            batch.Add(new LogIpAggregate
            {
                LogDate = parsedDate,
                Ip = reader.GetString(1),
                RequestCount = reader.GetInt64(2),
                Status2xxCount = reader.GetInt64(3),
                Status3xxCount = reader.GetInt64(4),
                Status4xxCount = reader.GetInt64(5),
                Status5xxCount = reader.GetInt64(6)
            });
            if (batch.Count >= boundedBatchSize)
            {
                yield return batch;
                batch = new List<LogIpAggregate>(boundedBatchSize);
            }
        }

        if (batch.Count > 0) yield return batch;
    }

    public async Task WriteCsvAsync(
        string workspaceId,
        IisLogFilterCriteria criteria,
        IReadOnlyList<LogRowSort> sorts,
        Stream output,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(GetPath(workspaceId), cancellationToken);
        var query = BuildWhere(criteria);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Date, Time, Ip, Method, Uri, Status, Referrer, UserAgent FROM LogRows {query.Where} ORDER BY {BuildOrderBy(sorts)};";
        AddParameters(command, query.Parameters);

        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 65536, leaveOpen: true);
        await writer.WriteLineAsync("date,time,ip,method,uri,status,referrer,user_agent".AsMemory(), cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new string[8];
            for (var column = 0; column < values.Length; column++) values[column] = Csv(reader.GetString(column));
            await writer.WriteLineAsync(string.Join(',', values).AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    public async Task DeleteAsync(string? workspaceId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(workspaceId, "N", out _))
        {
            return;
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = GetPath(workspaceId!) + suffix;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // An active reader may briefly hold a sidecar; expiry cleanup will retry later.
            }
        }
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root)) return;

        var threshold = DateTime.UtcNow - WorkspaceLifetime;
        foreach (var path in Directory.EnumerateFiles(_root, "*.sqlite"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(path) < threshold)
            {
                var id = Path.GetFileNameWithoutExtension(path);
                await DeleteAsync(id, cancellationToken);
            }
        }
    }

    private string GetPath(string workspaceId)
    {
        if (!Guid.TryParseExact(workspaceId, "N", out _))
        {
            throw new InvalidOperationException("Invalid IIS log workspace identifier.");
        }

        return Path.Combine(_root, $"{workspaceId}.sqlite");
    }

    private void EnsureAvailableSpace(long sourceBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(_root)) ?? _root;
        var available = new DriveInfo(root).AvailableFreeSpace;
        var required = Math.Max(1L * 1024 * 1024 * 1024, checked(sourceBytes * 2 + 512L * 1024 * 1024));
        if (available < required)
        {
            throw new IOException($"Not enough free disk space for a disk-backed log workspace. Need about {required / (1024d * 1024 * 1024):0.##} GB free.");
        }
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(string path, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 30
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        // Expiry is based on last use, not just upload completion, so actively browsed workspaces survive.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=30000; PRAGMA temp_store=FILE;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureDefaultPageIndexAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE INDEX IF NOT EXISTS IX_LogRows_Date_Time_Id ON LogRows(Date, Time, Id);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (string Where, List<SqliteParameter> Parameters) BuildWhere(IisLogFilterCriteria criteria)
    {
        var clauses = new List<string>();
        var parameters = new List<SqliteParameter>();
        AddContains("Date", criteria.Date);
        AddContains("Time", criteria.Time);
        AddContains("Ip", criteria.Ip);
        AddContains("Method", criteria.Method);
        AddContains("Uri", criteria.Uri);
        AddContains("Status", criteria.Status);
        AddContains("Referrer", criteria.Referrer);
        AddContains("UserAgent", criteria.UserAgent);
        AddContains("Uri", criteria.UrlQuery);
        AddContains("Method", criteria.MethodQuery);

        if (!string.IsNullOrWhiteSpace(criteria.StatusGroup))
        {
            clauses.Add("substr(Status, 1, length($statusGroup)) = $statusGroup");
            parameters.Add(new SqliteParameter("$statusGroup", criteria.StatusGroup.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(criteria.StatusCode))
        {
            clauses.Add("Status = $statusCode COLLATE NOCASE");
            parameters.Add(new SqliteParameter("$statusCode", criteria.StatusCode.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(criteria.TextQuery))
        {
            clauses.Add("(instr(lower(Ip), lower($text)) > 0 OR instr(lower(Uri), lower($text)) > 0 OR instr(lower(Referrer), lower($text)) > 0 OR instr(lower(UserAgent), lower($text)) > 0)");
            parameters.Add(new SqliteParameter("$text", criteria.TextQuery.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(criteria.ExcludeIpQuery))
        {
            var excludedIps = criteria.ExcludeIpQuery
                .Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (excludedIps.Length > 0)
            {
                var names = excludedIps.Select((_, index) => $"$excluded{index}").ToArray();
                clauses.Add($"Ip NOT IN ({string.Join(", ", names)})");
                for (var index = 0; index < excludedIps.Length; index++)
                {
                    parameters.Add(new SqliteParameter(names[index], excludedIps[index]));
                }
            }
        }

        return (clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}", parameters);

        void AddContains(string column, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var parameterName = $"$p{parameters.Count}";
            clauses.Add($"instr(lower({column}), lower({parameterName})) > 0");
            parameters.Add(new SqliteParameter(parameterName, value.Trim()));
        }
    }

    private static string BuildOrderBy(IReadOnlyList<LogRowSort> sorts)
    {
        var items = new List<string>();
        foreach (var sort in sorts)
        {
            var column = sort.Column switch
            {
                nameof(ParsedLogRow.Date) => "Date",
                nameof(ParsedLogRow.Time) => "Time",
                nameof(ParsedLogRow.Ip) => "Ip",
                nameof(ParsedLogRow.Method) => "Method",
                nameof(ParsedLogRow.Uri) => "Uri",
                nameof(ParsedLogRow.Status) => "Status",
                nameof(ParsedLogRow.Referrer) => "Referrer",
                nameof(ParsedLogRow.UserAgent) => "UserAgent",
                _ => null
            };
            if (column is not null) items.Add($"{column} {(sort.Ascending ? "ASC" : "DESC")}");
        }

        items.Add("Id ASC");
        return string.Join(", ", items);
    }

    private static void AddParameters(SqliteCommand command, IEnumerable<SqliteParameter> parameters)
    {
        foreach (var parameter in parameters) command.Parameters.Add(parameter);
    }

    private static async Task<List<KeyValuePair<string, long>>> GetGroupedCountsAsync(
        SqliteConnection connection,
        (string Where, List<SqliteParameter> Parameters) query,
        string column,
        int take,
        CancellationToken cancellationToken)
    {
        var validColumn = column is "Status" or "Uri" or "Ip" ? column : throw new ArgumentOutOfRangeException(nameof(column));
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {validColumn}, COUNT(*) FROM LogRows {query.Where} {(query.Where.Length == 0 ? "WHERE" : "AND")} {validColumn} <> '' GROUP BY {validColumn} ORDER BY COUNT(*) DESC, {validColumn} ASC {(take > 0 ? "LIMIT $take" : string.Empty)};";
        AddParameters(command, query.Parameters);
        if (take > 0) command.Parameters.AddWithValue("$take", take);

        var items = new List<KeyValuePair<string, long>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new KeyValuePair<string, long>(reader.GetString(0), reader.GetInt64(1)));
        }
        return items;
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    public sealed class WorkspaceWriter : IAsyncDisposable
    {
        private readonly string _path;
        private readonly SqliteConnection _connection;
        private readonly DateOnly _fallbackDate;
        private SqliteTransaction? _transaction;
        private int _uncommitted;
        private bool _completed;

        internal WorkspaceWriter(string id, string path, SqliteConnection connection, DateOnly fallbackDate)
        {
            Id = id;
            _path = path;
            _connection = connection;
            _fallbackDate = fallbackDate;
        }

        public string Id { get; }

        public async Task AppendAsync(IReadOnlyList<ParsedLogRow> rows, CancellationToken cancellationToken)
        {
            if (rows.Count == 0) return;
            _transaction ??= await _connection.BeginTransactionAsync(cancellationToken) as SqliteTransaction
                ?? throw new InvalidOperationException("Could not start IIS workspace transaction.");

            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = "INSERT INTO LogRows (Date, Time, Ip, Method, Uri, Status, Referrer, UserAgent) VALUES ($date, $time, $ip, $method, $uri, $status, $referrer, $agent);";
            var date = command.Parameters.Add("$date", SqliteType.Text);
            var time = command.Parameters.Add("$time", SqliteType.Text);
            var ip = command.Parameters.Add("$ip", SqliteType.Text);
            var method = command.Parameters.Add("$method", SqliteType.Text);
            var uri = command.Parameters.Add("$uri", SqliteType.Text);
            var status = command.Parameters.Add("$status", SqliteType.Text);
            var referrer = command.Parameters.Add("$referrer", SqliteType.Text);
            var agent = command.Parameters.Add("$agent", SqliteType.Text);
            await command.PrepareAsync(cancellationToken);

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                date.Value = DateOnly.TryParse(row.Date, out var parsedDate)
                    ? parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : _fallbackDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                time.Value = row.Time;
                ip.Value = row.Ip;
                method.Value = row.Method;
                uri.Value = row.Uri;
                status.Value = row.Status;
                referrer.Value = row.Referrer;
                agent.Value = row.UserAgent;
                await command.ExecuteNonQueryAsync(cancellationToken);
                _uncommitted++;
            }

            if (_uncommitted >= 10_000) await CommitAsync(cancellationToken);
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            await CommitAsync(cancellationToken);
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                CREATE INDEX IX_LogRows_Date_Id ON LogRows(Date, Id);
                CREATE INDEX IX_LogRows_Date_Time_Id ON LogRows(Date, Time, Id);
                CREATE INDEX IX_LogRows_Date_Ip ON LogRows(Date, Ip);
                CREATE INDEX IX_LogRows_Ip_Id ON LogRows(Ip, Id);
                CREATE INDEX IX_LogRows_Status_Id ON LogRows(Status, Id);
                PRAGMA journal_mode=DELETE;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await _connection.CloseAsync();
            _completed = true;
            File.SetLastWriteTimeUtc(_path, DateTime.UtcNow);
        }

        private async Task CommitAsync(CancellationToken cancellationToken)
        {
            if (_transaction is null) return;
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
            _uncommitted = 0;
            File.SetLastWriteTimeUtc(_path, DateTime.UtcNow);
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction is not null)
            {
                await _transaction.RollbackAsync();
                await _transaction.DisposeAsync();
            }
            await _connection.DisposeAsync();
            if (!_completed)
            {
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    try
                    {
                        var path = _path + suffix;
                        if (File.Exists(path)) File.Delete(path);
                    }
                    catch (IOException) { }
                }
            }
        }
    }
}
