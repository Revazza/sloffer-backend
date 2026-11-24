using Microsoft.EntityFrameworkCore;
using Sloffer.Data;

namespace Sloffer.Jobs.Dedup;

/// <summary>
/// Sets <see cref="Job.DedupKey"/> and <see cref="Job.DuplicateOfId"/> on jobs that are about to be saved
/// (new rows at insert, or old rows during the backfill). Only the given jobs are changed; stored jobs never are.
/// </summary>
public static class JobDeduplicator
{
    /// <summary>
    /// For each job (earliest <see cref="Job.PostedAt"/> first): computes the key, then links it to the earliest
    /// original with the same key whose <c>PostedAt</c> is within <paramref name="window"/> of the job's own
    /// <c>PostedAt</c>. Originals are stored jobs with a key and no <c>DuplicateOfId</c>, or earlier jobs in this
    /// batch that stayed originals, so a duplicate never points at another duplicate.
    /// </summary>
    /// <returns>How many jobs were marked as duplicates.</returns>
    public static async Task<int> AssignAsync(
        SlofferDbContext db, IReadOnlyCollection<Job> jobs, TimeSpan window, CancellationToken ct)
    {
        if (jobs.Count == 0)
            return 0;

        var ordered = jobs.OrderBy(j => j.PostedAt).ThenBy(j => j.Id).ToList();
        foreach (var job in ordered)
            job.DedupKey = JobDedup.NormalizeKey(job.Company, job.Title);

        // One query for every stored original that could match any job in the batch.
        var keys = ordered.Select(j => j.DedupKey!).Distinct().ToList();
        var batchIds = ordered.Select(j => j.Id).ToList();
        var from = ordered[0].PostedAt - window;
        var to = ordered[^1].PostedAt + window;
        var stored = await db.Jobs
            .AsNoTracking()
            .Where(j => j.DedupKey != null && keys.Contains(j.DedupKey) && j.DuplicateOfId == null
                && j.PostedAt >= from && j.PostedAt <= to && !batchIds.Contains(j.Id))
            .Select(j => new Original(j.Id, j.DedupKey!, j.PostedAt))
            .ToListAsync(ct);

        var originals = stored.ToLookup(o => o.Key).ToDictionary(g => g.Key, g => g.ToList());
        var duplicates = 0;

        foreach (var job in ordered)
        {
            var key = job.DedupKey!;
            if (!originals.TryGetValue(key, out var candidates))
                originals[key] = candidates = [];

            var match = candidates
                .Where(o => (o.PostedAt - job.PostedAt).Duration() <= window)
                .OrderBy(o => o.PostedAt)
                .ThenBy(o => o.Id)
                .FirstOrDefault();

            if (match is null)
            {
                job.DuplicateOfId = null;
                candidates.Add(new Original(job.Id, key, job.PostedAt));
            }
            else
            {
                job.DuplicateOfId = match.Id;
                duplicates++;
            }
        }

        return duplicates;
    }

    private sealed record Original(Guid Id, string Key, DateTimeOffset PostedAt);
}
