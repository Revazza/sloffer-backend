namespace Sloffer.Jobs.Fetching;

/// <summary>
/// A public, no-auth source of job listings (one implementation per website).
/// Register implementations as <c>IJobSource</c> in DI; <see cref="JobFetcher"/> picks them all up.
/// </summary>
public interface IJobSource
{
    /// <summary>Stable, unique source name. Stored as <c>Job.Source</c> and used as the key in <c>JobFetch:Sources</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Returns listings posted since <paramref name="since"/>. Throw on failure (HTTP 429/5xx, bad data):
    /// the fetcher logs it and skips the source until its next allowed run. Do not retry here.
    /// </summary>
    Task<IReadOnlyList<FetchedJob>> FetchRecentAsync(DateTimeOffset since, CancellationToken ct);
}
