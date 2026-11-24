namespace Sloffer.Jobs.Sources.Jobicy;

/// <summary>Settings for <see cref="JobicyJobSource"/>, bound from <c>JobFetch:Jobicy</c>.</summary>
public sealed class JobicyOptions
{
    public const string SectionName = "JobFetch:Jobicy";

    /// <summary>Jobicy asks clients not to poll more than once per hour.</summary>
    public static readonly TimeSpan MinAllowedInterval = TimeSpan.FromMinutes(60);

    /// <summary>Listings per request, 1-200.</summary>
    public int Count { get; set; } = 100;

    /// <summary>Region slug (<c>europe</c>, <c>usa</c>, <c>anywhere</c>, ...). Empty means no filter.</summary>
    public string? Geo { get; set; } = "europe";

    /// <summary>Industry slug (<c>engineering</c>, ...). Empty means no filter.</summary>
    public string? Industry { get; set; } = "engineering";

    /// <summary>Minimum time between two fetches. Must be at least <see cref="MinAllowedInterval"/>.</summary>
    public TimeSpan MinInterval { get; set; } = MinAllowedInterval;

    /// <summary>HTTP request timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    internal bool IsValid() =>
        Count is >= 1 and <= 200
        && MinInterval >= MinAllowedInterval
        && Timeout > TimeSpan.Zero;
}
