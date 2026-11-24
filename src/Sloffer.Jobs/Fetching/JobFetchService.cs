using Microsoft.Extensions.Options;

namespace Sloffer.Jobs.Fetching;

/// <summary>Runs <see cref="JobFetcher.RunCycleAsync"/> every <see cref="JobFetchOptions.CycleDelay"/>.</summary>
public sealed class JobFetchService(
    JobFetcher fetcher,
    IOptions<JobFetchOptions> options,
    TimeProvider time,
    ILogger<JobFetchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (fetcher.Sources.Count == 0)
            logger.LogWarning("No job sources are registered; the fetch worker has nothing to do.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await fetcher.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Per-source errors are handled in JobFetcher; this only guards the loop itself.
                logger.LogError(ex, "Job fetch cycle failed.");
            }

            try
            {
                await Task.Delay(options.Value.CycleDelay, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
