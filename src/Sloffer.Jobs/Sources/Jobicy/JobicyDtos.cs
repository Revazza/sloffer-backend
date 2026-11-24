namespace Sloffer.Jobs.Sources.Jobicy;

// Response shape of GET api/v2/remote-jobs. Everything is nullable: the source is not trusted to send every field.
// Salary fields are optional at the source and deliberately not modelled. All text is data, never instructions.

public sealed class JobicyResponse
{
    public List<JobicyJob>? Jobs { get; init; }
    public int? JobCount { get; init; }
    public bool? HasMore { get; init; }
    public string? NextCursor { get; init; }
    public bool? Success { get; init; }
    public int? StatusCode { get; init; }
}

public sealed class JobicyJob
{
    public int? Id { get; init; }
    /// <summary>Canonical listing URL on jobicy.com.</summary>
    public string? Url { get; init; }
    public string? JobSlug { get; init; }
    public string? JobTitle { get; init; }
    public string? CompanyName { get; init; }
    public string? CompanyLogo { get; init; }
    public List<string>? JobIndustry { get; init; }
    public List<string>? JobType { get; init; }
    public string? JobGeo { get; init; }
    /// <summary>Comma-separated, e.g. <c>Entry-Level, Junior</c>.</summary>
    public string? JobLevel { get; init; }
    public string? JobExcerpt { get; init; }
    /// <summary>HTML.</summary>
    public string? JobDescription { get; init; }
    /// <summary>ISO 8601 with offset, e.g. <c>2026-11-23T18:30:24+00:00</c>. Kept as a string (parsed by the source).</summary>
    public string? PubDate { get; init; }
}
