using System.Text.RegularExpressions;
using System.Net;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class AnalysisSection(string title)
{
    public string Title { get; } = title;
    public List<string> Lines { get; } = [];
}

public sealed record LogTrafficPath(string Path, long Requests, long UniqueIps, long ErrorResponses, long UriVariants);

public static class LogAnalysisPresentationService
{
    private static readonly HashSet<string> DisplayedSectionTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "공격 URL", "의심 IP", "차단 후보", "대응 방안"
    };

    private static readonly string[] KnownSectionTitles =
    [
        "전체 요약", "위험도", "공격 URL", "의심 IP", "차단 후보", "대응 방안"
    ];

    public static List<AnalysisSection> ParseSections(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var sections = new List<AnalysisSection>();
        AnalysisSection? current = null;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var sectionTitle = NormalizeSectionTitle(line);
            if (sectionTitle is not null)
            {
                current = new AnalysisSection(sectionTitle);
                sections.Add(current);
                continue;
            }

            if (current is null)
            {
                current = new AnalysisSection("분석 결과");
                sections.Add(current);
            }

            current.Lines.Add(line);
        }

        return sections;
    }

    public static List<AnalysisSection> ParseDisplayedSections(string? text) => ParseSections(text)
        .Where(section => DisplayedSectionTitles.Contains(section.Title))
        .ToList();

    public static List<string> GetPreviewLines(AnalysisSection section)
    {
        const int maxCharacters = 150;
        var lines = section.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (section.Title.Equals("의심 IP", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = lines.Where(line => line.StartsWith("- ", StringComparison.Ordinal)).ToList();
            if (candidates.Count > 0)
            {
                return candidates.Select(line =>
                    Truncate(string.Join(" · ", line[2..].Split('·', StringSplitOptions.TrimEntries).Take(4)), maxCharacters)).ToList();
            }
        }

        if (section.Title.Equals("대응 방안", StringComparison.OrdinalIgnoreCase))
        {
            return lines.SelectMany(line => Regex.Split(line, @"(?<=[.!?])\s+"))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(3)
                .Select(line => Truncate(line.Trim(), maxCharacters))
                .ToList();
        }

        return lines.Take(4).Select(line => Truncate(line.TrimStart('-', ' '), maxCharacters)).ToList();
    }

    private static string Truncate(string value, int maxCharacters) =>
        value.Length <= maxCharacters ? value : value[..maxCharacters].TrimEnd() + "…";

    public static List<string> ParseBlockCandidateIps(string? report)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in ParseSections(report))
        {
            var candidateSection = section.Title == "차단 후보";
            var actionSection = section.Title == "대응 후보";
            var suspiciousSection = section.Title == "의심 IP";
            if (!candidateSection && !actionSection && !suspiciousSection) continue;

            foreach (var line in section.Lines)
            {
                if (!candidateSection && !line.Contains("차단", StringComparison.OrdinalIgnoreCase) && !line.Contains("Block", StringComparison.OrdinalIgnoreCase)) continue;
                var candidateText = line;
                if (candidateSection)
                {
                    var nextActionIndex = new[] { "속도 제한", "RateLimit", "모니터링", "Monitor" }
                        .Select(action => line.IndexOf(action, StringComparison.OrdinalIgnoreCase))
                        .Where(index => index >= 0)
                        .DefaultIfEmpty(line.Length)
                        .Min();
                    candidateText = line[..nextActionIndex];
                }
                foreach (var token in candidateText.Split([',', ';', ' ', '\t', '(', ')', '[', ']', '·'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var value = token.TrimStart('-', '*').TrimEnd('.', ':');
                    if ((value.Contains('.') || value.Contains(':')) && IPAddress.TryParse(value, out var address)) candidates.Add(address.ToString());
                }
            }
        }

        return candidates.ToList();
    }

    public static List<LogTrafficPath> SummarizePaths(IEnumerable<ParsedLogRow> rows, int take = 15) => rows
        .Where(row => !string.IsNullOrWhiteSpace(row.Uri))
        .GroupBy(row => NormalizePath(row.Uri), StringComparer.OrdinalIgnoreCase)
        .Select(group => new LogTrafficPath(
            group.Key,
            group.LongCount(),
            group.Select(row => row.Ip).Where(ip => !string.IsNullOrWhiteSpace(ip)).Distinct(StringComparer.OrdinalIgnoreCase).LongCount(),
            group.LongCount(row => row.Status.StartsWith("4", StringComparison.Ordinal) || row.Status.StartsWith("5", StringComparison.Ordinal)),
            group.Select(row => row.Uri).Distinct(StringComparer.OrdinalIgnoreCase).LongCount()))
        .OrderByDescending(path => path.Requests)
        .ThenBy(path => path.Path, StringComparer.OrdinalIgnoreCase)
        .Take(Math.Max(1, take))
        .ToList();

    private static string NormalizePath(string uri)
    {
        var path = uri.Trim();
        var queryIndex = path.IndexOf('?');
        return queryIndex >= 0 ? path[..queryIndex] : path;
    }

    public static List<string> ParseCandidateIps(string? report, string label)
    {
        if (string.IsNullOrWhiteSpace(report) || string.IsNullOrWhiteSpace(label))
            return [];

        var line = report
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(value => value.Trim())
            .FirstOrDefault(value => value.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(line))
            return [];

        var value = line[(label.Length + 1)..].Trim();
        if (string.Equals(value, "없음", StringComparison.OrdinalIgnoreCase))
            return [];

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeSectionTitle(string line)
    {
        var normalized = line.Trim();
        while (normalized.StartsWith('#') || normalized.StartsWith('*') || normalized.StartsWith('_') || normalized.StartsWith('`'))
            normalized = normalized[1..].TrimStart();

        while (normalized.EndsWith('*') || normalized.EndsWith('_') || normalized.EndsWith('`'))
            normalized = normalized[..^1].TrimEnd();

        if (normalized.StartsWith('[') && normalized.EndsWith(']') && normalized.Length > 2)
            return normalized[1..^1].Trim();

        return KnownSectionTitles.FirstOrDefault(title =>
            string.Equals(normalized, title, StringComparison.OrdinalIgnoreCase));
    }
}
