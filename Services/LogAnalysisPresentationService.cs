namespace Monitoring.Blazor.Services;

public sealed class AnalysisSection(string title)
{
    public string Title { get; } = title;
    public List<string> Lines { get; } = [];
}

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
