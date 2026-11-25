using Microsoft.Extensions.Options;
using RestEase;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Matching;

namespace Sloffer.Jobs.Sources.Arbeitnow;

/// <summary>
/// Arbeitnow job board (mostly Germany/EMEA). Pages from 1, waiting <c>DelayBetweenRequests</c> between requests,
/// and stops after a page with no listing inside the window, on the last page, or at <c>MaxPages</c>. The API has
/// no date filter and orders only roughly by <c>created_at</c>, so every listing is checked against
/// <c>since</c>. Listings without any of the required keywords are dropped. Listings missing a slug, URL, title,
/// company or <c>created_at</c> are skipped.
/// </summary>
public sealed class ArbeitnowJobSource(
    IArbeitnowClient api,
    IOptionsMonitor<ArbeitnowOptions> options,
    TimeProvider time,
    ILogger<ArbeitnowJobSource> logger) : IJobSource
{
    public const string SourceName = "Arbeitnow";

    private const string RemoteLabel = "Remote";

    public string Name => SourceName;

    public async Task<IReadOnlyList<FetchedJob>> FetchRecentAsync(DateTimeOffset since, CancellationToken ct)
    {
        var settings = options.CurrentValue;
        var keywords = settings.EffectiveRequiredKeywords;
        var matcher = keywords.Count > 0 ? new TermMatcher(keywords) : null;

        var result = new List<FetchedJob>();
        int pages = 0, total = 0, invalid = 0, inWindow = 0, withoutKeyword = 0;

        for (var page = 1; page <= settings.MaxPages; page++)
        {
            if (page > 1 && settings.DelayBetweenRequests > TimeSpan.Zero)
                await Task.Delay(settings.DelayBetweenRequests, time, ct);

            var response = await GetPageAsync(page, ct);
            pages++;

            var jobs = response?.Data ?? [];
            total += jobs.Count;
            var pageInWindow = 0;

            foreach (var job in jobs)
            {
                var mapped = Map(job);
                if (mapped is null)
                {
                    invalid++;
                    continue;
                }

                if (mapped.PostedAt < since)
                    continue;

                pageInWindow++;
                if (matcher is null || HasKeyword(matcher, job!))
                    result.Add(mapped);
                else
                    withoutKeyword++;
            }

            inWindow += pageInWindow;

            if (pageInWindow == 0 || string.IsNullOrWhiteSpace(response?.Links?.Next))
                break;
        }

        if (invalid > 0)
            logger.LogWarning("Arbeitnow: skipped {Skipped} of {Total} listings with missing or invalid fields.", invalid, total);

        logger.LogInformation(
            "Arbeitnow: read {Pages} page(s), {InWindow} listings in window, skipped {WithoutKeyword} without a required keyword, kept {Kept}.",
            pages, inWindow, withoutKeyword, result.Count);

        return result;
    }

    private async Task<ArbeitnowResponse?> GetPageAsync(int page, CancellationToken ct)
    {
        try
        {
            return await api.GetJobsAsync(page, ct);
        }
        catch (ApiException ex)
        {
            // Surface as HttpRequestException so the fetcher logs the status code.
            throw new HttpRequestException(
                $"Arbeitnow returned HTTP {(int)ex.StatusCode} ({ex.ReasonPhrase}) for page {page}.", ex, ex.StatusCode);
        }
    }

    private static bool HasKeyword(TermMatcher matcher, ArbeitnowJob job) =>
        matcher.Any(job.Title ?? "")
        || (job.Tags ?? []).Any(t => t is not null && matcher.Any(t))
        || matcher.Any(job.Description ?? "");

    private static FetchedJob? Map(ArbeitnowJob? job)
    {
        if (job is null
            || string.IsNullOrWhiteSpace(job.Slug)
            || string.IsNullOrWhiteSpace(job.Url)
            || string.IsNullOrWhiteSpace(job.Title)
            || string.IsNullOrWhiteSpace(job.CompanyName)
            || !TryConvertCreatedAt(job.CreatedAt, out var postedAt))
        {
            return null;
        }

        var types = job.JobTypes?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();

        return new FetchedJob(
            Source: SourceName,
            ExternalId: job.Slug.Trim(),
            Url: job.Url.Trim(),
            Title: job.Title.Trim(),
            Company: job.CompanyName.Trim(),
            Location: Location(job.Location, job.Remote == true),
            EmploymentTypes: types is { Count: > 0 } ? string.Join(", ", types) : null,
            Level: null,
            Description: NullIfEmpty(job.Description),
            PostedAt: postedAt);
    }

    /// <summary>
    /// Remote listings get <c>Remote</c> in their location so the scorer's region rules see it. Arbeitnow often
    /// sends <c>Remote</c> as the location already; it is not appended twice.
    /// </summary>
    internal static string? Location(string? location, bool remote)
    {
        var trimmed = NullIfEmpty(location?.Trim());
        if (!remote)
            return trimmed;
        if (trimmed is null)
            return RemoteLabel;
        return trimmed.Contains(RemoteLabel, StringComparison.OrdinalIgnoreCase) ? trimmed : $"{trimmed}, {RemoteLabel}";
    }

    private static bool TryConvertCreatedAt(long? seconds, out DateTimeOffset postedAt)
    {
        if (seconds is { } s
            && s >= DateTimeOffset.MinValue.ToUnixTimeSeconds()
            && s <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            postedAt = DateTimeOffset.FromUnixTimeSeconds(s);
            return true;
        }

        postedAt = default;
        return false;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
