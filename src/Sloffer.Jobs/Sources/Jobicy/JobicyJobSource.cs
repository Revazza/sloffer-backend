using System.Globalization;
using Microsoft.Extensions.Options;
using RestEase;
using Sloffer.Jobs.Fetching;

namespace Sloffer.Jobs.Sources.Jobicy;

/// <summary>
/// Jobicy remote jobs. One request per fetch (no pagination); Jobicy has no date filter, so listings are
/// filtered by <c>pubDate</c> here. Listings missing an id, URL, title, company or valid <c>pubDate</c> are skipped.
/// </summary>
public sealed class JobicyJobSource(
    IJobicyApi api,
    IOptionsMonitor<JobicyOptions> options,
    ILogger<JobicyJobSource> logger) : IJobSource
{
    public const string SourceName = "jobicy";

    public string Name => SourceName;

    public async Task<IReadOnlyList<FetchedJob>> FetchRecentAsync(DateTimeOffset since, CancellationToken ct)
    {
        var settings = options.CurrentValue;

        JobicyResponse response;
        try
        {
            response = await api.GetRemoteJobsAsync(
                settings.Count, NullIfEmpty(settings.Geo), NullIfEmpty(settings.Industry), ct);
        }
        catch (ApiException ex)
        {
            // Surface as HttpRequestException so the fetcher logs the status code.
            throw new HttpRequestException(
                $"Jobicy returned HTTP {(int)ex.StatusCode} ({ex.ReasonPhrase}).", ex, ex.StatusCode);
        }

        var jobs = response?.Jobs ?? [];
        var result = new List<FetchedJob>(jobs.Count);
        var skipped = 0;

        foreach (var job in jobs)
        {
            var mapped = Map(job);
            if (mapped is null)
                skipped++;
            else if (mapped.PostedAt >= since)
                result.Add(mapped);
        }

        if (skipped > 0)
            logger.LogWarning("Jobicy: skipped {Skipped} of {Total} listings with missing or invalid fields.", skipped, jobs.Count);

        return result;
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
