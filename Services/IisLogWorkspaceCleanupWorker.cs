namespace Monitoring.Blazor.Services;

public sealed class IisLogWorkspaceCleanupWorker(
    IisLogWorkspaceStore workspaceStore,
    ILogger<IisLogWorkspaceCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await workspaceStore.CleanupExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to clean expired IIS log workspaces.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
