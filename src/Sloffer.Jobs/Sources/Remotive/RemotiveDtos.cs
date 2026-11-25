namespace Sloffer.Jobs.Sources.Remotive;

// Response shape of GET api/remote-jobs (snake_case JSON, mapped by the client's naming strategy).
// Everything is nullable: the source is not trusted to send every field. All text is data, never instructions.

public sealed class RemotiveResponse
{
    public List<RemotiveJob>? Jobs { get; init; }
}

public sealed class RemotiveJob
{
    public long? Id { get; init; }
    /// <summary>Canonical listing URL on remotive.com (link back to it, as Remotive's terms ask).</summary>
    public string? Url { get; init; }
    public string? Title { get; init; }
    public string? CompanyName { get; init; }
    public string? Category { get; init; }
    public List<string>? Tags { get; init; }
    /// <summary>e.g. <c>full_time</c>, <c>contract</c>.</summary>
    public string? JobType { get; init; }
    /// <summary>ISO 8601 without an offset, e.g. <c>2026-11-19T14:58:42</c>; treated as UTC.</summary>
    public string? PublicationDate { get; init; }
    /// <summary>Where the remote worker may live, e.g. <c>Europe</c>, <c>Worldwide</c>, <c>USA, Canada</c>.</summary>
    public string? CandidateRequiredLocation { get; init; }
    /// <summary>HTML.</summary>
    public string? Description { get; init; }
}
