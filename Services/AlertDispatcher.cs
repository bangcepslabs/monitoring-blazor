using System.Threading.Channels;

namespace Monitoring.Blazor.Services;

public sealed class AlertDispatcher
{
    private readonly Channel<AlertMessage> _channel = Channel.CreateUnbounded<AlertMessage>();

    public bool Enqueue(AlertMessage message) => _channel.Writer.TryWrite(message);

    public ChannelReader<AlertMessage> Reader => _channel.Reader;
}
