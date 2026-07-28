using System.Threading.Channels;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class MonitoringSnapshotQueue
{
    private readonly Channel<MonitoringInfo> _channel = Channel.CreateUnbounded<MonitoringInfo>();

    public bool Enqueue(MonitoringInfo info) => _channel.Writer.TryWrite(info);

    public ChannelReader<MonitoringInfo> Reader => _channel.Reader;
}
