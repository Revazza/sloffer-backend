using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sloffer.Data;

namespace Sloffer.Jobs.Fetching;

/// <summary>
/// One fetch cycle over all registered sources. Keeps the last fetch time per source in memory, so it must be a
/// singleton. A failing source is logged and waits for its next allowed run; there are no retries.
/// </summary>
public sealed class JobFetcher(
    IEnumerable<IJobSource> sources,
    IServiceScopeFactory scopes,
    IOptions<JobFetchOptions> options,
    TimeProvider time,
    ILogger<JobFetcher> logger)
{
    /// <summary>Only jobs posted within this window are requested and stored.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly IReadOnlyList<IJobSource> _sources = sources.ToList();
    private readonly Dictionary<string, DateTimeOffset> _lastFetch = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IJobSource> Sources => _sources;

    /// <summary>Fetches every source whose MinInterval has elapsed. Returns the number of new jobs stored.</summary>
    public async Task<int> RunCycleAsync(CancellationToken ct)
    {
        var inserted = 0;
        foreach (var source in _sources)
        {
            ct.ThrowIfCancellationRequested();

            var now = time.GetUtcNow();
            if (_lastFetch.TryGetValue(source.Name, out var last) && now - last < options.Value.GetMinInterval(source.Name))
                continue;

            // Recorded before fetching so a failure also waits for the next allowed run.
            _lastFetch[source.Name] = now;

            try
            {
                inserted += await FetchSourceAsync(source, now, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fetching jobs from source {Source} failed; skipping it until its next run.", source.Name);
            }
        }

        return inserted;
    }

    private async Task<int> FetchSourceAsync(IJobSource source, DateTimeOffset now, CancellationToken ct)
    {
        var since = now - MaxAge;
        var fetched = await source.FetchRecentAsync(since, ct);

        var recent = fetched
            .Where(job => !string.IsNullOrWhiteSpace(job.ExternalId) && job.PostedAt >= since)
            .DistinctBy(job => job.ExternalId)
            .ToList();

        // One scope (and DbContext) per source run.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SlofferDbContext>();

        var externalIds = recent.Select(job => job.ExternalId).ToList();
        var existing = await db.Jobs
            .Where(job => job.Source == source.Name && externalIds.Contains(job.ExternalId))
            .Select(job => job.ExternalId)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var newJobs = recent
            .Where(job => !existingSet.Contains(job.ExternalId))
            .Select(job => new Job
            {
                Id = Guid.NewGuid(),
                // The source's registered name is the dedupe key, whatever the record says.
                Source = source.Name,
                ExternalId = job.ExternalId,
                Url = job.Url,
                Title = job.Title,
                Company = job.Company,
                Location = job.Location,
                EmploymentTypes = job.EmploymentTypes,
                Level = job.Level,
                Description = job.Description,
                PostedAt = job.PostedAt.ToUniversalTime(),
                FetchedAt = now,
            })
            .ToList();

        db.Jobs.AddRange(newJobs);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Source {Source}: {Fetched} fetched, {Recent} within {MaxAge}, {Inserted} new.",
            source.Name, fetched.Count, recent.Count, MaxAge, newJobs.Count);
        return newJobs.Count;
    }
}
