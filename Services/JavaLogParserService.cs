using System.Text.RegularExpressions;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class JavaLogParserService
{
    private static readonly Regex SpringPattern = new(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2}[T\s]\d{2}:\d{2}:\d{2}(?:[.,]\d{3})?)\s+(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL)\s+(?<pid>\d+)?\s*---\s+\[(?<thread>[^\]]+)\]\s+(?<logger>[\w.$-]+)\s*:\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StandardPattern = new(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2}[T\s]\d{2}:\d{2}:\d{2}(?:[.,]\d{3})?)\s+(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL)\s+\[(?<thread>[^\]]+)\]\s+(?<logger>[\w.$-]+)\s*[-:]\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TomcatPattern = new(
        @"^(?<timestamp>[A-Z][a-z]{2}\s+\d{1,2},\s+\d{4}\s+\d{1,2}:\d{2}:\d{2}\s+[AP]M)\s+(?<logger>[\w.$-]+)\s+(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL):\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public List<JavaLogEntry> ParseLines(IEnumerable<string> lines, string sourceFile)
    {
        var entries = new List<JavaLogEntry>();
        JavaLogEntry? current = null;

        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var line = raw.TrimEnd();
            if (LooksLikeContinuation(line) && current is not null)
            {
                current.Message = string.IsNullOrWhiteSpace(current.Message)
                    ? line.Trim()
                    : $"{current.Message}{Environment.NewLine}{line.Trim()}";
                continue;
            }

            if (TryParseLine(line, sourceFile, out var entry))
            {
                current = entry;
                entries.Add(entry);
                continue;
            }

            if (current is not null)
            {
                current.Message = string.IsNullOrWhiteSpace(current.Message)
                    ? line.Trim()
                    : $"{current.Message} {line.Trim()}";
            }
        }

        return entries;
    }

    private static bool TryParseLine(string line, string sourceFile, out JavaLogEntry entry)
    {
        var match = SpringPattern.Match(line);
        if (!match.Success)
        {
            match = StandardPattern.Match(line);
        }

        if (!match.Success)
        {
            match = TomcatPattern.Match(line);
        }

        if (!match.Success)
        {
            entry = new JavaLogEntry();
            return false;
        }

        entry = new JavaLogEntry
        {
            Timestamp = match.Groups["timestamp"].Value.Trim(),
            Level = NormalizeLevel(match.Groups["level"].Value),
            Thread = match.Groups["thread"].Success ? match.Groups["thread"].Value.Trim() : string.Empty,
            Logger = match.Groups["logger"].Success ? match.Groups["logger"].Value.Trim() : string.Empty,
            Message = match.Groups["message"].Success ? match.Groups["message"].Value.Trim() : string.Empty,
            SourceFile = sourceFile
        };

        return true;
    }

    private static bool LooksLikeContinuation(string line)
        => line.StartsWith(' ') || line.StartsWith('\t') || line.StartsWith("at ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Caused by:", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return "INFO";
        }

        var normalized = level.Trim().ToUpperInvariant();
        return normalized == "WARNING" ? "WARN" : normalized;
    }
}
