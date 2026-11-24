using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sloffer.Data;
using Sloffer.Jobs.Fetching;

namespace Sloffer.Jobs.Dedup;

/// <summary>
/// Computes <c>DedupKey</c> and <c>DuplicateOfId</c> for rows stored before de-duplication existed (<c>DedupKey</c>
/// null), oldest <c>PostedAt</c> first, in batches. Runs once at worker startup when
/// <c>JobFetch:BackfillDedupOnStartup</c> is true; it is not a loop. Safe to run twice: rows that already have a
/// key are never touched again. Registered before the fetch worker, so it finishes before the first fetch cycle.
/// </summary>
public sealed class JobDedupBackfillService(
    IServiceScopeFactory scopes,
    IOptions<JobFetchOptions> options,
    ILogger<JobDedupBackfillService> logger) : IHostedService
{
    public const int BatchSize = 500;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.BackfillDedupOnStartup)
            return;

        try
        {
            await BackfillAsync(BatchSize, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // E.g. the database is down. Never stop the worker over the backfill; rows stay as they are.
            logger.LogError(ex, "Job de-duplication backfill failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Backfills every row without a <c>DedupKey</c>. Returns the number of rows updated.</summary>
    public async Task<int> BackfillAsync(int batchSize, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        var window = options.Value.MaxAge;
        var updated = 0;
        var duplicates = 0;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();

        while (true)
        {
            // Saved rows get a key and drop out of this filter, so the next batch is the next rows.
            var batch = await db.Jobs
                .Where(j => j.DedupKey == null)
                .OrderBy(j => j.PostedAt)
                .ThenBy(j => j.Id)
                .Take(batchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;

            duplicates += await JobDeduplicator.AssignAsync(db, batch, window, ct);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            updated += batch.Count;
        }

        logger.LogInformation("De-duplication backfill updated {Count} jobs ({Duplicates} marked as duplicates).", updated, duplicates);
        return updated;
    }
}
