using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class IisLogFilterCriteria
{
    public string StatusGroup { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public string UrlQuery { get; init; } = string.Empty;
    public string MethodQuery { get; init; } = string.Empty;
    public string TextQuery { get; init; } = string.Empty;
    public string ExcludeIpQuery { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public string Ip { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Uri { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Referrer { get; init; } = string.Empty;
    public string UserAgent { get; init; } = string.Empty;
}

public sealed class LogRowSort(string column, bool ascending)
{
    public string Column { get; } = column;
    public bool Ascending { get; set; } = ascending;
}

public static class IisLogQueryService
{
    public static List<ParsedLogRow> FilterAndSort(
        IEnumerable<ParsedLogRow> rows,
        IisLogFilterCriteria criteria,
        IReadOnlyList<LogRowSort> sorts)
    {
        IEnumerable<ParsedLogRow> query = rows;

        if (!string.IsNullOrWhiteSpace(criteria.StatusGroup))
            query = query.Where(row => !string.IsNullOrWhiteSpace(row.Status) && row.Status.StartsWith(criteria.StatusGroup, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(criteria.StatusCode))
            query = query.Where(row => string.Equals(row.Status, criteria.StatusCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(criteria.UrlQuery))
            query = query.Where(row => row.Uri.Contains(criteria.UrlQuery.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(criteria.MethodQuery))
            query = query.Where(row => row.Method.Contains(criteria.MethodQuery.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(criteria.TextQuery))
        {
            var key = criteria.TextQuery.Trim();
            query = query.Where(row =>
                row.Ip.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                row.Uri.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                row.Referrer.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                row.UserAgent.Contains(key, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(criteria.ExcludeIpQuery))
        {
            var excludedIps = ParseIpList(criteria.ExcludeIpQuery);
            if (excludedIps.Count > 0)
                query = query.Where(row => !excludedIps.Contains(row.Ip));
        }

        query = ApplyContainsFilter(query, criteria.Date, row => row.Date);
        query = ApplyContainsFilter(query, criteria.Time, row => row.Time);
        query = ApplyContainsFilter(query, criteria.Ip, row => row.Ip);
        query = ApplyContainsFilter(query, criteria.Method, row => row.Method);
        query = ApplyContainsFilter(query, criteria.Uri, row => row.Uri);
        query = ApplyContainsFilter(query, criteria.Status, row => row.Status);
        query = ApplyContainsFilter(query, criteria.Referrer, row => row.Referrer);
        query = ApplyContainsFilter(query, criteria.UserAgent, row => row.UserAgent);

        return ApplySort(query.ToList(), sorts);
    }

    private static IEnumerable<ParsedLogRow> ApplyContainsFilter(
        IEnumerable<ParsedLogRow> rows,
        string value,
        Func<ParsedLogRow, string> selector)
    {
        if (string.IsNullOrWhiteSpace(value))
            return rows;

        var key = value.Trim();
        return rows.Where(row => selector(row).Contains(key, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> ParseIpList(string input) => input
        .Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<ParsedLogRow> ApplySort(List<ParsedLogRow> rows, IReadOnlyList<LogRowSort> sorts)
    {
        if (sorts.Count == 0)
            return rows;

        IOrderedEnumerable<ParsedLogRow>? ordered = null;
        foreach (var sort in sorts)
        {
            Func<ParsedLogRow, string> selector = sort.Column switch
            {
                nameof(ParsedLogRow.Time) => row => row.Time,
                nameof(ParsedLogRow.Ip) => row => row.Ip,
                nameof(ParsedLogRow.Method) => row => row.Method,
                nameof(ParsedLogRow.Uri) => row => row.Uri,
                nameof(ParsedLogRow.Status) => row => row.Status,
                nameof(ParsedLogRow.Referrer) => row => row.Referrer,
                nameof(ParsedLogRow.UserAgent) => row => row.UserAgent,
                _ => row => row.Date
            };

            ordered = ordered is null
                ? (sort.Ascending
                    ? rows.OrderBy(selector, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderByDescending(selector, StringComparer.OrdinalIgnoreCase))
                : (sort.Ascending
                    ? ordered.ThenBy(selector, StringComparer.OrdinalIgnoreCase)
                    : ordered.ThenByDescending(selector, StringComparer.OrdinalIgnoreCase));
        }

        return ordered?.ToList() ?? rows;
    }
}
