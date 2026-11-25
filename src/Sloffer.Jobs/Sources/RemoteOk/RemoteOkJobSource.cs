using Microsoft.Extensions.Options;
using RestEase;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Matching;

namespace Sloffer.Jobs.Sources.RemoteOk;

/// <summary>
/// Remote OK board (latest ~100 remote jobs, one request per fetch). The legal-notice element is skipped; listings
/// posted before <c>since</c>, listings without a required .NET keyword, and listings missing an id, URL, position,
/// company or epoch are dropped. Every Remote OK job is remote; its location is where the worker may live (often empty).
/// </summary>
public sealed class RemoteOkJobSource(
    IRemoteOkClient api,
    IOptionsMonitor<RemoteOkOptions> options,
    ILogger<RemoteOkJobSource> logger) : IJobSource
{
    /// <summary>Remote OK asks to be named exactly "Remote OK".</summary>
    public const string SourceName = "Remote OK";

    public string Name => SourceName;

    public async Task<IReadOnlyList<FetchedJob>> FetchRecentAsync(DateTimeOffset since, CancellationToken ct)
    {
        var keywords = options.CurrentValue.EffectiveRequiredKeywords;
        var matcher = keywords.Count > 0 ? new TermMatcher(keywords) : null;

        List<RemoteOkJob>? response;
        try
        {
            response = await api.GetJobsAsync(ct);
        }
        catch (ApiException ex)
        {
            // Surface as HttpRequestException so the fetcher logs the status code.
            throw new HttpRequestException(
                $"Remote OK returned HTTP {(int)ex.StatusCode} ({ex.ReasonPhrase}).", ex, ex.StatusCode);
        }

        var jobs = (response ?? []).Where(j => j is not null && j.Legal is null).ToList();
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

            if (matcher is not null && !HasKeyword(matcher, job))
            {
                withoutKeyword++;
                continue;
            }

            result.Add(mapped);
        }

        if (invalid > 0)
            logger.LogWarning("Remote OK: skipped {Skipped} of {Total} listings with missing or invalid fields.", invalid, jobs.Count);

        logger.LogInformation(
            "Remote OK: {Total} listings, {Old} older than the window, skipped {WithoutKeyword} without a required keyword, kept {Kept}.",
            jobs.Count, old, withoutKeyword, result.Count);

        return result;
    }

    private static bool HasKeyword(TermMatcher matcher, RemoteOkJob job) =>
        matcher.Any(job.Position ?? "")
        || (job.Tags ?? []).Any(t => t is not null && matcher.Any(t))
        || matcher.Any(job.Description ?? "");

    private static FetchedJob? Map(RemoteOkJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Id)
            || string.IsNullOrWhiteSpace(job.Url)
            || string.IsNullOrWhiteSpace(job.Position)
            || string.IsNullOrWhiteSpace(job.Company)
            || job.Epoch is not { } epoch
            || epoch < DateTimeOffset.MinValue.ToUnixTimeSeconds()
            || epoch > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return null;
        }

        return new FetchedJob(
            Source: SourceName,
            ExternalId: job.Id.Trim(),
            Url: job.Url.Trim(),
            Title: job.Position.Trim(),
            Company: job.Company.Trim(),
            Location: NullIfEmpty(job.Location?.Trim()),
            EmploymentTypes: null,
            Level: null,
            Description: NullIfEmpty(job.Description),
            PostedAt: DateTimeOffset.FromUnixTimeSeconds(epoch));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
