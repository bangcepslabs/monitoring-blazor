using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

/// <summary>
/// Shared IIS row aggregation rules used by manual uploads and scheduled imports.
/// </summary>
public static class IisLogAggregationService
{
    public static Dictionary<(DateOnly LogDate, string Ip), LogIpAggregate> CreateAggregateMap(
        IEnumerable<ParsedLogRow> rows,
        DateTime fallbackUtc)
    {
        var aggregates = new Dictionary<(DateOnly LogDate, string Ip), LogIpAggregate>();
        AddRows(aggregates, rows, fallbackUtc);
        return aggregates;
    }

    public static void AddRows(
        IDictionary<(DateOnly LogDate, string Ip), LogIpAggregate> aggregates,
        IEnumerable<ParsedLogRow> rows,
        DateTime fallbackUtc)
    {
        foreach (var row in rows)
        {
            var logDate = DateOnly.TryParse(row.Date, out var parsedDate)
                ? parsedDate
                : DateOnly.FromDateTime(fallbackUtc);
            var ip = row.Ip ?? string.Empty;
            var key = (logDate, ip);

            if (!aggregates.TryGetValue(key, out var aggregate))
            {
                aggregate = new LogIpAggregate { LogDate = logDate, Ip = ip };
                aggregates.Add(key, aggregate);
            }

            aggregate.RequestCount++;
            if (!int.TryParse(row.Status, out var status))
            {
                continue;
            }

            if (status is >= 200 and <= 299) aggregate.Status2xxCount++;
            else if (status is >= 300 and <= 399) aggregate.Status3xxCount++;
            else if (status is >= 400 and <= 499) aggregate.Status4xxCount++;
            else if (status is >= 500 and <= 599) aggregate.Status5xxCount++;
        }
    }
}
