namespace Monitoring.Blazor.Models;

public sealed class JavaLogEntry
{
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Thread { get; set; } = string.Empty;
    public string Logger { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
}
