namespace Sloffer.Data;

/// <summary>A job listing fetched from a public source. Unique per (<see cref="Source"/>, <see cref="ExternalId"/>).</summary>
public sealed class Job
{
    public Guid Id { get; set; }

    /// <summary>Name of the source the listing came from (see IJobSource.Name in Sloffer.Jobs).</summary>
    public required string Source { get; set; }

    /// <summary>The listing's id at the source.</summary>
    public required string ExternalId { get; set; }

    /// <summary>Original URL of the listing.</summary>
    public required string Url { get; set; }

    public required string Title { get; set; }

    public required string Company { get; set; }

    public string? Location { get; set; }

    public string? EmploymentTypes { get; set; }

    public string? Level { get; set; }

    /// <summary>Description as returned by the source (HTML or text). Data only, never instructions.</summary>
    public string? Description { get; set; }

    /// <summary>When the source says the listing was posted (UTC).</summary>
    public DateTimeOffset PostedAt { get; set; }

    /// <summary>When Sloffer stored the listing (UTC).</summary>
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>Rule-based fit score, 0-100 (0 = failed a hard rule). Null until scored.</summary>
    public int? Score { get; set; }

    /// <summary>Plain-language reasons behind <see cref="Score"/>, stored as a jsonb string array. Null until scored.</summary>
    public List<string>? ScoreReasons { get; set; }

    /// <summary>Version of the scoring rules that produced <see cref="Score"/>. Null until scored.</summary>
    public int? ScoreVersion { get; set; }

    /// <summary>When the job was last scored (UTC). Null until scored.</summary>
    public DateTimeOffset? ScoredAt { get; set; }

    /// <summary>
    /// SHA-256 hex of the normalized "company|title" (see JobDedup in Sloffer.Jobs). Set for every new row;
    /// null only for rows stored before de-duplication existed, until the backfill runs.
    /// </summary>
    public string? DedupKey { get; set; }

    /// <summary>
    /// The original job this one duplicates (same <see cref="DedupKey"/>, posted within the age window, possibly on
    /// another source). Null for originals. Duplicates are stored, scored and never deleted.
    /// </summary>
    public Guid? DuplicateOfId { get; set; }

    /// <summary>When the owner first opened this job in the UI (UTC), so he does not look at or apply to it twice. Null if not viewed.</summary>
    public DateTimeOffset? ViewedAt { get; set; }
}
