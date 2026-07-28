namespace Monitoring.Blazor.Services;

public sealed class LogAutoExportWorker(
    LogAutoExportService exportService,
    ILogger<LogAutoExportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = exportService.GetDelayToNextRun();
            await Task.Delay(delay, stoppingToken);

            try
            {
                await exportService.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Log auto export failed.");
            }
        }
    }
}
