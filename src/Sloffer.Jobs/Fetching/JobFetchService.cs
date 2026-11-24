using Microsoft.Extensions.Options;

namespace Sloffer.Jobs.Fetching;

/// <summary>Runs <see cref="JobFetcher"/> cycles until shutdown, waiting <c>JobFetch:CycleDelay</c> between them.</summary>
public sealed class JobFetchService(
    JobFetcher fetcher,
    IOptionsMonitor<JobFetchOptions> options,
    TimeProvider time,
    ILogger<JobFetchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job fetching started (cycle delay {CycleDelay}).", options.CurrentValue.CycleDelay);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await fetcher.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // E.g. the database is down. Never let one bad cycle stop the host.
                logger.LogError(ex, "Job fetch cycle failed.");
            }

            try
            {
                await Task.Delay(options.CurrentValue.CycleDelay, time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
