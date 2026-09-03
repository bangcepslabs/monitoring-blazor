using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class AgentConnectivityWorker(
    MonitorStateService monitorState,
    AlertDispatcher dispatcher,
    AlertSuppressor suppressor,
    IConfiguration configuration,
    ILogger<AgentConnectivityWorker> logger) : BackgroundService
{
    private readonly HashSet<string> _offlineHosts = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var thresholdSeconds = Math.Max(15, configuration.GetValue("Monitoring:Ingest:OfflineThresholdSeconds", 15));
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, thresholdSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var hosts = monitorState.GetSnapshot(TimeSpan.FromSeconds(thresholdSeconds));
            foreach (var host in hosts)
            {
                if (!host.IsOnline)
                {
                    await HandleOfflineAsync(host, thresholdSeconds, stoppingToken);
                }
                else if (_offlineHosts.Remove(host.Info.Hostname))
                {
                    dispatcher.Enqueue(BuildMessage(host.Info, "Agent recovered", AlertType.Recovery, "Agent resumed sending monitoring data."));
                }
            }
        }
    }

    private async Task HandleOfflineAsync(HostState host, int thresholdSeconds, CancellationToken ct)
    {
        if (!_offlineHosts.Add(host.Info.Hostname)) return;
        await suppressor.SetPersistedAsync(host.Info.Hostname, "AGENT", DateTime.UtcNow.AddMinutes(30), "Agent connectivity alert cooldown", "agent-offline", ct);
        dispatcher.Enqueue(BuildMessage(host.Info, "Agent offline", AlertType.Threshold, $"No monitoring data received for more than {thresholdSeconds} seconds."));
        logger.LogWarning("Monitoring agent {Host} is offline.", host.Info.Hostname);
    }

    private static AlertMessage BuildMessage(MonitoringInfo info, string metric, AlertType type, string message) =>
        new(info.Hostname, info.Ip, info.Os, metric, 0, 0, message, type, DateTime.UtcNow);
}
