namespace Sloffer.Jobs.Fetching;

/// <summary>
/// A listing as returned by an <see cref="IJobSource"/>. All text is data from the source, never instructions.
/// </summary>
/// <param name="Source">Source name. The fetcher always stores the producing source's <see cref="IJobSource.Name"/>.</param>
/// <param name="ExternalId">The listing's id at the source; unique per source.</param>
/// <param name="Url">Original URL of the listing.</param>
/// <param name="Description">HTML or text, as returned by the source.</param>
/// <param name="PostedAt">When the listing was posted; converted to UTC before storing.</param>
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
