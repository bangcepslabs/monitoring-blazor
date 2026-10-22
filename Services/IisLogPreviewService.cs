using Microsoft.AspNetCore.Components.Forms;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class IisLogPreviewService(IisLogWorkspaceStore workspaceStore)
{
    private const int ParseBatchSize = 500;
    private const int MaxAggregateGroups = 500_000;
    private const long MaxPreviewCharacters = 16L * 1024 * 1024;
    private static readonly SemaphoreSlim ParseGate = new(1, 1);
    public const int MaxPreviewRows = 50_000;

    public sealed record ParseResult(
        string WorkspaceId,
        List<ParsedLogRow> PreviewRows,
        List<LogIpAggregate> Aggregates,
        long TotalRows,
        bool IsPreviewTruncated);

    public async Task<ParseResult> ParseFilesAsync(
        IReadOnlyList<IBrowserFile> files,
        long maxFileSize,
        DateTime fallbackUtc,
        Func<string, double, Task> reportProgressAsync,
        CancellationToken cancellationToken,
        bool retainPreviewRows = true,
        bool retainAggregates = true)
    {
        await reportProgressAsync("Waiting for another IIS log batch to finish", 0);
        await ParseGate.WaitAsync(cancellationToken);
        try
        {
            var sourceBytes = files.Sum(file => (long)file.Size);
            await using var writer = await workspaceStore.CreateAsync(sourceBytes, fallbackUtc, cancellationToken);
            var result = await ParseFilesCoreAsync(files, maxFileSize, fallbackUtc, writer, reportProgressAsync, cancellationToken, retainPreviewRows, retainAggregates);
            await reportProgressAsync("Building searchable log workspace", 88);
            await writer.CompleteAsync(cancellationToken);
            await reportProgressAsync("Workspace ready", 94);
            return result with { WorkspaceId = writer.Id };
        }
        finally
        {
            ParseGate.Release();
        }
    }

    private async Task<ParseResult> ParseFilesCoreAsync(
        IReadOnlyList<IBrowserFile> files,
        long maxFileSize,
        DateTime fallbackUtc,
        IisLogWorkspaceStore.WorkspaceWriter writer,
        Func<string, double, Task> reportProgressAsync,
        CancellationToken cancellationToken,
        bool retainPreviewRows,
        bool retainAggregates)
    {
        var state = new ParseState(retainPreviewRows, retainAggregates);
        var totalBytes = files.Sum(file => (long)file.Size);
        var bytesBefore = 0L;

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            await reportProgressAsync($"Parsing {file.Name}", 5 + (index * 80.0 / Math.Max(files.Count, 1)));

            await ParseFileAsync(
                file,
                bytesBefore,
                totalBytes,
                maxFileSize,
                fallbackUtc,
                writer,
                state,
                reportProgressAsync,
                cancellationToken);
            bytesBefore += file.Size;
        }

        return new ParseResult(
            string.Empty,
            state.PreviewRows,
            state.Aggregates.Values.ToList(),
            state.TotalRows,
            state.TotalRows > state.PreviewRows.Count);
    }

    private async Task ParseFileAsync(
        IBrowserFile file,
        long bytesBefore,
        long totalBytes,
        long maxFileSize,
        DateTime fallbackUtc,
        IisLogWorkspaceStore.WorkspaceWriter writer,
        ParseState state,
        Func<string, double, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
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
                var fraction = Math.Clamp((bytesBefore + stream.Position) / (double)totalBytes, 0, 1);
                var current = 5 + (int)(fraction * 80);
                if (current != lastProgress)
                {
                    lastProgress = current;
                    await reportProgressAsync($"Parsing {file.Name}", current);
                }
            }

            if (lines.Count >= ParseBatchSize)
            {
                var rows = ProcessBatch(lines, ref fieldOrder, state, fallbackUtc);
                await writer.AppendAsync(rows, cancellationToken);
                lines.Clear();
            }
        }

        if (lines.Count > 0)
        {
            var rows = ProcessBatch(lines, ref fieldOrder, state, fallbackUtc);
            await writer.AppendAsync(rows, cancellationToken);
        }
    }

    private static List<ParsedLogRow> ProcessBatch(
        IEnumerable<string> lines,
        ref string[]? fieldOrder,
        ParseState state,
        DateTime fallbackUtc)
    {
        var rows = ApacheLogParser.ParseLines(lines, ref fieldOrder);
        if (state.RetainAggregates)
        {
            IisLogAggregationService.AddRows(state.Aggregates, rows, fallbackUtc);
            if (state.Aggregates.Count > MaxAggregateGroups)
            {
                throw new InvalidDataException(
                    $"This batch contains more than {MaxAggregateGroups:N0} unique date/IP groups. Split it into smaller uploads to protect server memory.");
            }
        }

        state.TotalRows += rows.Count;

        if (state.CanAddPreview)
        {
            foreach (var row in rows)
            {
                var rowCharacters = (long)row.Date.Length + row.Time.Length + row.Ip.Length + row.Method.Length + row.Uri.Length + row.Status.Length + row.Referrer.Length + row.UserAgent.Length;
                if (state.PreviewRows.Count >= MaxPreviewRows || state.PreviewCharacters + rowCharacters > MaxPreviewCharacters)
                {
                    state.CanAddPreview = false;
                    break;
                }

                state.PreviewRows.Add(row);
                state.PreviewCharacters += rowCharacters;
            }
        }

        return rows;
    }

    private sealed class ParseState(bool retainPreviewRows, bool retainAggregates)
    {
        public List<ParsedLogRow> PreviewRows { get; } = retainPreviewRows ? new(MaxPreviewRows) : [];
        public Dictionary<(DateOnly LogDate, string Ip), LogIpAggregate> Aggregates { get; } = [];
        public bool RetainAggregates { get; } = retainAggregates;
        public long TotalRows { get; set; }
        public long PreviewCharacters { get; set; }
        public bool CanAddPreview { get; set; } = retainPreviewRows;
    }
}
