namespace Monitoring.Blazor.Models;

public sealed class LogIpAggregate
{
    public required DateOnly LogDate { get; set; }
    public required string Ip { get; set; }
    public long RequestCount { get; set; }
    public long Status2xxCount { get; set; }
    public long Status3xxCount { get; set; }
    public long Status4xxCount { get; set; }
    public long Status5xxCount { get; set; }
}
