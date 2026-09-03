using Monitoring.Blazor.Services;

namespace Monitoring.Blazor.Services;

public sealed class ServerMonitorWorker(
    ILogger<ServerMonitorWorker> logger,
    SystemInfoCollector collector,
    MonitorStateService state,
    MonitoringHealthState healthState) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var serverInfo = await collector.GetServerInfoAsync(stoppingToken);
                state.UpdateServer(serverInfo);
                healthState.MarkSuccess("server-monitor");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to collect server info.");
                healthState.MarkFailure("server-monitor", ex);
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
