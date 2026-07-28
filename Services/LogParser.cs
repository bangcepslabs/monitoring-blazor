using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public static class ApacheLogParser
{
    public static List<ParsedLogRow> ParseLines(IEnumerable<string> lines)
    {
        var rows = new List<ParsedLogRow>();
        string[]? fieldOrder = null;

        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var line = raw.Trim();
            if (line.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
            {
                var header = line[8..].Trim();
                fieldOrder = header.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            if (fieldOrder is not null && parts.Length == fieldOrder.Length)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < fieldOrder.Length; i++)
                {
                    map[fieldOrder[i]] = parts[i];
                }

                var uriStem = ReadField(map, "cs-uri-stem");
                var uriQuery = ReadField(map, "cs-uri-query");
                var uri = uriStem;
                if (!string.IsNullOrWhiteSpace(uriQuery) && uriQuery != "-")
                {
                    uri = $"{uriStem}?{uriQuery}";
                }

                rows.Add(new ParsedLogRow
                {
                    Date = ReadField(map, "date"),
                    Time = ReadField(map, "time"),
                    Ip = ReadField(map, "c-ip"),
                    Method = ReadField(map, "cs-method"),
                    Uri = uri,
                    Status = ReadField(map, "sc-status"),
                    Referrer = ReadField(map, "cs(Referer)"),
                    UserAgent = ReadField(map, "cs(User-Agent)")
                });

                continue;
            }

            // Legacy fallback for fixed-position parser.
            if (parts.Length < 14)
            {
                continue;
            }

            rows.Add(new ParsedLogRow
            {
                Date = parts[0],
                Time = parts[1],
                Ip = parts[8],
                Method = parts[3],
                Uri = parts[4],
                Status = parts[11],
                Referrer = parts[10],
                UserAgent = parts[9]
            });
        }

        return rows;
    }

    private static string ReadField(Dictionary<string, string> map, string key)
    {
        if (!map.TryGetValue(key, out var value))
        {
            return string.Empty;
        }

        return value == "-" ? string.Empty : value;
    }
}
