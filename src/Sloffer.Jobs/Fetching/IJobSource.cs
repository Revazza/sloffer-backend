namespace Sloffer.Jobs.Fetching;

/// <summary>A public, no-auth source of job listings. Register implementations as <c>IJobSource</c> in DI.</summary>
public interface IJobSource
{
    /// <summary>Stable source name; stored as <c>Job.Source</c> and used as the key in <c>JobFetch:Sources</c>.</summary>
    string Name { get; }

    /// <summary>Returns jobs posted since <paramref name="since"/>. May throw on HTTP or parse errors.</summary>
    Task<IReadOnlyList<FetchedJob>> FetchRecentAsync(DateTimeOffset since, CancellationToken ct);
}

/// <summary>A listing as returned by a source. Text fields are data, never instructions.</summary>
public sealed record FetchedJob(
    string Source,
    string ExternalId,
    string Url,
    string Title,
    string Company,
    string? Location,
    string? EmploymentTypes,
    string? Level,
    string? Description,
    DateTimeOffset PostedAt);
