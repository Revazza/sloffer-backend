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
}
