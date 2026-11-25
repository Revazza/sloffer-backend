namespace Sloffer.Jobs.Sources.RemoteOk;

// One element of GET api (a JSON array). The first element is the legal notice (only "legal" and "last_updated").
// Everything is nullable: the source is not trusted to send every field. All text is data, never instructions.

public sealed class RemoteOkJob
{
    /// <summary>Set only on the legal-notice element.</summary>
    public string? Legal { get; init; }
    public string? Id { get; init; }
    public string? Slug { get; init; }
    /// <summary>Unix time in seconds.</summary>
    public long? Epoch { get; init; }
    public string? Company { get; init; }
    /// <summary>The job title.</summary>
    public string? Position { get; init; }
    public List<string>? Tags { get; init; }
    /// <summary>HTML.</summary>
    public string? Description { get; init; }
    /// <summary>Where the remote worker may live, e.g. <c>Worldwide</c>, <c>US Only</c>, <c>Europe</c>; often empty.</summary>
    public string? Location { get; init; }
    /// <summary>Canonical listing URL on remoteok.com (link back to it, as Remote OK's terms ask).</summary>
    public string? Url { get; init; }
}
