using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sloffer.Data;
using Sloffer.Jobs.Matching;

namespace Sloffer.Jobs.Fetching;

public enum SourceFetchStatus
{
    /// <summary><c>MinInterval</c> has not elapsed since the source's last fetch.</summary>
    Skipped,
    Succeeded,
    /// <summary>The source (or storing its jobs) threw; it waits for its next allowed run. No retry.</summary>
    Failed,
}

/// <summary>Outcome of one source in one cycle.</summary>
public sealed record SourceFetchResult(string Source, SourceFetchStatus Status, int Fetched = 0, int Inserted = 0);

/// <summary>
/// Runs one fetch cycle over every registered <see cref="IJobSource"/>: rate-limits each source by its
/// <c>MinInterval</c>, drops listings older than <see cref="MaxAge"/>, and inserts only new (Source, ExternalId) pairs.
/// Singleton: it remembers when each source was last fetched (in memory).
/// </summary>
public sealed class JobFetcher(
    IServiceScopeFactory scopes,
    JobScorer scorer,
    IOptionsMonitor<JobFetchOptions> options,
    TimeProvider time,
    ILogger<JobFetcher> logger)
{
    /// <summary>Only listings posted within this window are requested and stored.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly Dictionary<string, DateTimeOffset> _lastRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cycleLock = new(1, 1);

    public async Task<IReadOnlyList<SourceFetchResult>> RunCycleAsync(CancellationToken ct)
    {
        await _cycleLock.WaitAsync(ct);
        try
        {
            // One scope per run: sources and the DbContext come from it.
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();
            var sources = scope.ServiceProvider.GetServices<IJobSource>().ToList();
            var settings = options.CurrentValue;

            var results = new List<SourceFetchResult>(sources.Count);
            foreach (var source in sources)
                results.Add(await RunSourceAsync(source, db, settings, ct));

            return results;
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    private async Task<SourceFetchResult> RunSourceAsync(
        IJobSource source, SlofferDbContext db, JobFetchOptions settings, CancellationToken ct)
    {
        var name = source.Name;
        var now = time.GetUtcNow();
        var minInterval = settings.GetMinInterval(name);

        if (_lastRuns.TryGetValue(name, out var lastRun) && now - lastRun < minInterval)
        {
            logger.LogDebug("Skipping job source {Source}: next run allowed at {NextRun:O}.", name, lastRun + minInterval);
            return new SourceFetchResult(name, SourceFetchStatus.Skipped);
        }

        // Recorded before the call, so a failing source also waits a full MinInterval (no retry loop).
        _lastRuns[name] = now;
        var since = now - MaxAge;

        try
        {
            var fetched = await source.FetchRecentAsync(since, ct);
            var inserted = await StoreAsync(db, name, fetched, since, now, ct);

            logger.LogInformation("Job source {Source}: fetched {Fetched}, inserted {Inserted}.", name, fetched.Count, inserted);
            return new SourceFetchResult(name, SourceFetchStatus.Succeeded, fetched.Count, inserted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            var statusCode = (ex as HttpRequestException)?.StatusCode;
            logger.LogWarning(ex,
                "Job source {Source} failed (HTTP status: {StatusCode}); skipped until {NextRun:O}.",
                name, statusCode?.ToString() ?? "n/a", now + minInterval);
            return new SourceFetchResult(name, SourceFetchStatus.Failed);
        }
    }

    private async Task<int> StoreAsync(
        SlofferDbContext db,
        string sourceName,
        IReadOnlyList<FetchedJob> fetched,
        DateTimeOffset since,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var recent = fetched
            .Where(j => !string.IsNullOrWhiteSpace(j.ExternalId) && j.PostedAt >= since)
            .DistinctBy(j => j.ExternalId, StringComparer.Ordinal)
            .ToList();

        if (recent.Count == 0)
            return 0;

        var externalIds = recent.Select(j => j.ExternalId).ToList();
        var existing = await db.Jobs
            .Where(j => j.Source == sourceName && externalIds.Contains(j.ExternalId))
            .Select(j => j.ExternalId)
            .ToListAsync(ct);
        var existingIds = existing.ToHashSet(StringComparer.Ordinal);

        var newJobs = recent
            .Where(j => !existingIds.Contains(j.ExternalId))
            .Select(j => ScoredJob(j, new Job
            {
                Id = Guid.CreateVersion7(now),
                Source = sourceName,
                ExternalId = j.ExternalId,
                Url = j.Url,
                Title = j.Title,
                Company = j.Company,
                Location = j.Location,
                EmploymentTypes = j.EmploymentTypes,
                Level = j.Level,
                Description = j.Description,
                PostedAt = j.PostedAt.ToUniversalTime(),
                FetchedAt = now,
            }, now))
            .ToList();

        if (newJobs.Count == 0)
            return 0;

        db.Jobs.AddRange(newJobs);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return newJobs.Count;
    }

    /// <summary>Every new job is scored at insert time.</summary>
    private Job ScoredJob(FetchedJob fetched, Job job, DateTimeOffset now)
    {
        JobRescoreService.Apply(job, scorer.Score(fetched), now);
        return job;
    }
}
