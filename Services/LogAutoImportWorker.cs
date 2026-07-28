namespace Monitoring.Blazor.Services;

public sealed class LogAutoImportWorker(
    LogAutoImportService importService,
    ILogger<LogAutoImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = importService.GetDelayToNextRun();
            await Task.Delay(delay, stoppingToken);

            try
            {
                await importService.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Log auto import failed.");
            }
        }
    }
}
