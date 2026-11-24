using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sloffer.Data;
using Sloffer.Jobs.Fetching;

namespace Sloffer.Jobs.Matching;

/// <summary>
/// Rescores stored jobs whose <c>ScoreVersion</c> differs from <see cref="JobScorer.Version"/> (including unscored
/// rows), in batches. Runs once at worker startup when <c>JobScoring:RescoreOnStartup</c> is true; it is not a loop.
/// Registered before the fetch worker, so it finishes before the first fetch cycle starts.
/// </summary>
public sealed class JobRescoreService(
    IServiceScopeFactory scopes,
    JobScorer scorer,
    IOptions<JobScoringOptions> options,
    TimeProvider time,
    ILogger<JobRescoreService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.RescoreOnStartup)
            return;

        try
        {
            await RescoreOutdatedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // E.g. the database is down. Never stop the worker over a rescore; rows stay as they are.
            logger.LogError(ex, "Job rescore on startup failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Rescores every row with an outdated or missing score. Returns the number of rows updated.</summary>
    public async Task<int> RescoreOutdatedAsync(CancellationToken ct)
    {
        var batchSize = options.Value.RescoreBatchSize;
        var total = 0;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();

        while (true)
        {
            // Saved rows get the current version and drop out of this filter, so the next batch is the next rows.
            var batch = await db.Jobs
                .Where(j => j.ScoreVersion == null || j.ScoreVersion != JobScorer.Version)
                .OrderBy(j => j.Id)
                .Take(batchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;

            var now = time.GetUtcNow();
            foreach (var job in batch)
                Apply(job, scorer.Score(ToFetchedJob(job)), now);

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            total += batch.Count;
        }

        logger.LogInformation("Rescored {Count} jobs to score version {Version}.", total, JobScorer.Version);
        return total;
    }

    /// <summary>Copies a score onto a job entity.</summary>
    public static void Apply(Job job, ScoreResult result, DateTimeOffset scoredAt)
    {
        job.Score = result.Score;
        job.ScoreReasons = [.. result.Reasons];
        job.ScoreVersion = result.Version;
        job.ScoredAt = scoredAt;
    }

    private static FetchedJob ToFetchedJob(Job job) =>
        new(job.Source, job.ExternalId, job.Url, job.Title, job.Company, job.Location, job.EmploymentTypes, job.Level,
            job.Description, job.PostedAt);
}
