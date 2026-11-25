using System.Globalization;
using Microsoft.Extensions.Options;
using RestEase;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Matching;

namespace Sloffer.Jobs.Sources.Remotive;

/// <summary>
/// Remotive remote-jobs board. One request per fetch for the configured category; listings published before
/// <c>since</c>, listings without a required .NET keyword, and listings missing an id, URL, title, company or
/// publication date are dropped. Every Remotive job is remote; its location is where the worker may live.
/// </summary>
public sealed class RemotiveJobSource(
    IRemotiveClient api,
    IOptionsMonitor<RemotiveOptions> options,
    ILogger<RemotiveJobSource> logger) : IJobSource
{
    public const string SourceName = "Remotive";

    public string Name => SourceName;

    public async Task<IReadOnlyList<FetchedJob>> FetchRecentAsync(DateTimeOffset since, CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var keywords = settings.EffectiveRequiredKeywords;
        var matcher = keywords.Count > 0 ? new TermMatcher(keywords) : null;

        RemotiveResponse? response;
        try
        {
            response = await api.GetJobsAsync(settings.Category, ct);
        }
        catch (ApiException ex)
        {
            // Surface as HttpRequestException so the fetcher logs the status code.
            throw new HttpRequestException(
                $"Remotive returned HTTP {(int)ex.StatusCode} ({ex.ReasonPhrase}).", ex, ex.StatusCode);
        }

        var jobs = response?.Jobs ?? [];
        var result = new List<FetchedJob>();
        int invalid = 0, old = 0, withoutKeyword = 0;

        foreach (var job in jobs)
        {
            var mapped = Map(job);
            if (mapped is null)
            {
                invalid++;
                continue;
            }

            if (mapped.PostedAt < since)
            {
                old++;
                continue;
            }

            if (matcher is not null && !HasKeyword(matcher, job!))
            {
                withoutKeyword++;
                continue;
            }

            result.Add(mapped);
        }

        if (invalid > 0)
            logger.LogWarning("Remotive: skipped {Skipped} of {Total} listings with missing or invalid fields.", invalid, jobs.Count);

        logger.LogInformation(
            "Remotive: {Total} listings in {Category}, {Old} older than the window, skipped {WithoutKeyword} without a required keyword, kept {Kept}.",
            jobs.Count, settings.Category, old, withoutKeyword, result.Count);

        return result;
    }

    private static bool HasKeyword(TermMatcher matcher, RemotiveJob job) =>
        matcher.Any(job.Title ?? "")
        || (job.Tags ?? []).Any(t => t is not null && matcher.Any(t))
        || matcher.Any(job.Description ?? "");

    private static FetchedJob? Map(RemotiveJob? job)
    {
        if (job?.Id is not { } id
            || string.IsNullOrWhiteSpace(job.Url)
            || string.IsNullOrWhiteSpace(job.Title)
            || string.IsNullOrWhiteSpace(job.CompanyName)
            || !TryParsePublicationDate(job.PublicationDate, out var postedAt))
        {
            return null;
        }

        return new FetchedJob(
            Source: SourceName,
            ExternalId: id.ToString(CultureInfo.InvariantCulture),
            Url: job.Url.Trim(),
            Title: job.Title.Trim(),
            Company: job.CompanyName.Trim(),
            Location: NullIfEmpty(job.CandidateRequiredLocation?.Trim()),
            EmploymentTypes: NullIfEmpty(EmploymentType(job.JobType)),
            Level: null,
            Description: NullIfEmpty(job.Description),
            PostedAt: postedAt);
    }

    /// <summary><c>full_time</c> -> <c>Full-Time</c>.</summary>
    public static string? EmploymentType(string? jobType) =>
        string.IsNullOrWhiteSpace(jobType)
            ? null
            : string.Join("-", jobType.Trim().Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>Remotive sends no offset; the time is UTC.</summary>
    private static bool TryParsePublicationDate(string? value, out DateTimeOffset postedAt) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out postedAt);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
