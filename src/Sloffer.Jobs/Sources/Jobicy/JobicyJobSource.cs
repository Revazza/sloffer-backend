using System.Globalization;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;
using RestEase;
using Sloffer.Jobs.Fetching;

namespace Sloffer.Jobs.Sources.Jobicy;

/// <summary>
/// Jobicy remote jobs. Runs each configured query (no pagination) one after another, waiting
/// <c>DelayBetweenRequests</c> between requests, and merges the results, keeping the first listing per Jobicy id.
/// A failing query is logged and skipped; the fetch throws only if every query failed (no retries). Jobicy has no
/// date filter, so listings are filtered by <c>pubDate</c> here. Listings missing an id, URL, title, company or valid
/// <c>pubDate</c> are skipped.
/// </summary>
public sealed class JobicyJobSource(
    IJobicyApi api,
    IOptionsMonitor<JobicyOptions> options,
    TimeProvider time,
    ILogger<JobicyJobSource> logger) : IJobSource
{
    public const string SourceName = "jobicy";

    public string Name => SourceName;

    public async Task<IReadOnlyList<FetchedJob>> FetchRecentAsync(DateTimeOffset since, CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var queries = settings.EffectiveQueries;

        var result = new List<FetchedJob>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        ExceptionDispatchInfo? lastFailure = null;
        int failed = 0, total = 0, skipped = 0, duplicates = 0;

        for (var i = 0; i < queries.Count; i++)
        {
            if (i > 0 && settings.DelayBetweenRequests > TimeSpan.Zero)
                await Task.Delay(settings.DelayBetweenRequests, time, ct);

            var query = queries[i];
            JobicyResponse? response;
            try
            {
                response = await GetAsync(settings.Count, query, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Any failure (HTTP 429/5xx, timeout, malformed body) drops only this query.
                failed++;
                lastFailure = ExceptionDispatchInfo.Capture(ex);
                logger.LogWarning(ex, "Jobicy: query {Query} failed; keeping results of the other queries.", query.ToString());
                continue;
            }

            var jobs = response?.Jobs ?? [];
            total += jobs.Count;

            foreach (var job in jobs)
            {
                var mapped = Map(job);
                if (mapped is null)
                    skipped++;
                else if (mapped.PostedAt < since)
                    continue;
                else if (!seenIds.Add(mapped.ExternalId))
                    duplicates++;
                else
                    result.Add(mapped);
            }
        }

        if (lastFailure is not null && failed == queries.Count)
            lastFailure.Throw();

        if (skipped > 0)
            logger.LogWarning("Jobicy: skipped {Skipped} of {Total} listings with missing or invalid fields.", skipped, total);

        logger.LogInformation(
            "Jobicy: ran {Queries} queries ({Failed} failed), dropped {Duplicates} duplicate listings, kept {Kept}.",
            queries.Count, failed, duplicates, result.Count);

        return result;
    }

    private async Task<JobicyResponse?> GetAsync(int count, JobicyQuery query, CancellationToken ct)
    {
        try
        {
            return await api.GetRemoteJobsAsync(count, query.GeoOrNull, query.IndustryOrNull, query.TagOrNull, ct);
        }
        catch (ApiException ex)
        {
            // Surface as HttpRequestException so the fetcher logs the status code.
            throw new HttpRequestException(
                $"Jobicy returned HTTP {(int)ex.StatusCode} ({ex.ReasonPhrase}) for query {query}.", ex, ex.StatusCode);
        }
    }

    private static FetchedJob? Map(JobicyJob? job)
    {
        if (job?.Id is not { } id
            || string.IsNullOrWhiteSpace(job.Url)
            || string.IsNullOrWhiteSpace(job.JobTitle)
            || string.IsNullOrWhiteSpace(job.CompanyName)
            || !TryParsePubDate(job.PubDate, out var postedAt))
        {
            return null;
        }

        var types = job.JobType?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();

        return new FetchedJob(
            Source: SourceName,
            ExternalId: id.ToString(CultureInfo.InvariantCulture),
            Url: job.Url.Trim(),
            Title: job.JobTitle.Trim(),
            Company: job.CompanyName.Trim(),
            Location: NullIfEmpty(job.JobGeo?.Trim()),
            EmploymentTypes: types is { Count: > 0 } ? string.Join(", ", types) : null,
            Level: NullIfEmpty(job.JobLevel?.Trim()),
            Description: NullIfEmpty(job.JobDescription),
            PostedAt: postedAt);
    }

    private static bool TryParsePubDate(string? value, out DateTimeOffset postedAt)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            postedAt = parsed.ToUniversalTime();
            return true;
        }

        postedAt = default;
        return false;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
