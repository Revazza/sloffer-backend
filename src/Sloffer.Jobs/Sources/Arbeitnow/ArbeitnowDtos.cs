namespace Sloffer.Jobs.Sources.Arbeitnow;

// Response shape of GET api/job-board-api (snake_case JSON, mapped by the client's naming strategy).
// Everything is nullable: the source is not trusted to send every field. All text is data, never instructions.

public sealed class ArbeitnowResponse
{
    public List<ArbeitnowJob>? Data { get; init; }
    public ArbeitnowLinks? Links { get; init; }
    public ArbeitnowMeta? Meta { get; init; }
}

public sealed class ArbeitnowJob
{
    /// <summary>Unique listing slug, e.g. <c>senior-net-developer-berlin-123456</c>.</summary>
    public string? Slug { get; init; }
    public string? CompanyName { get; init; }
    public string? Title { get; init; }
    /// <summary>HTML (entity-encoded at the source).</summary>
    public string? Description { get; init; }
    public bool? Remote { get; init; }
    /// <summary>Canonical listing URL on arbeitnow.com.</summary>
    public string? Url { get; init; }
    public List<string>? Tags { get; init; }
    public List<string>? JobTypes { get; init; }
    public string? Location { get; init; }
    /// <summary>Unix time in seconds.</summary>
    public long? CreatedAt { get; init; }
}

public sealed class ArbeitnowLinks
{
    public string? First { get; init; }
    public string? Last { get; init; }
    public string? Prev { get; init; }
    /// <summary>URL of the next page; null on the last page.</summary>
    public string? Next { get; init; }
}

public sealed class ArbeitnowMeta
{
    public int? CurrentPage { get; init; }
    public int? PerPage { get; init; }
}
