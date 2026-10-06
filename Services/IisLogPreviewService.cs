using Microsoft.AspNetCore.Components.Forms;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public static class IisLogPreviewService
{
    private const int ParseBatchSize = 500;

    public static async Task<List<ParsedLogRow>> ParseFilesAsync(
        IReadOnlyList<IBrowserFile> files,
        long maxFileSize,
        Func<string, double, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var results = new List<ParsedLogRow>();
        var totalBytes = files.Sum(file => (long)file.Size);
        var bytesBefore = 0L;

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            await reportProgressAsync($"Parsing {file.Name}", 5 + (index * 90.0 / Math.Max(files.Count, 1)));

            var rows = await ParseFileAsync(
                file,
                bytesBefore,
                totalBytes,
                maxFileSize,
                reportProgressAsync,
                cancellationToken);
            results.AddRange(rows);
            bytesBefore += file.Size;
        }

        return results;
    }

    private static async Task<List<ParsedLogRow>> ParseFileAsync(
        IBrowserFile file,
        long bytesBefore,
        long totalBytes,
        long maxFileSize,
        Func<string, double, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var rows = new List<ParsedLogRow>();
        var lines = new List<string>(ParseBatchSize);
        string[]? fieldOrder = null;
        var lastProgress = -1;

        await using var stream = file.OpenReadStream(maxFileSize, cancellationToken);
        using var reader = new StreamReader(stream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            lines.Add(line);
            if (totalBytes > 0)
            {
                var current = (int)(((bytesBefore + stream.Position) / (double)totalBytes) * 100);
                if (current != lastProgress)
                {
                    lastProgress = current;
                    await reportProgressAsync($"Parsing {file.Name}", current);
                }
            }

            if (lines.Count >= ParseBatchSize)
            {
                rows.AddRange(ApacheLogParser.ParseLines(lines, ref fieldOrder));
                lines.Clear();
            }
        }

        if (lines.Count > 0)
        {
            rows.AddRange(ApacheLogParser.ParseLines(lines, ref fieldOrder));
        }

        return rows;
    }
}
