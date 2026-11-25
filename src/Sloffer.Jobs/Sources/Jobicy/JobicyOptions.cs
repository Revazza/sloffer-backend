namespace Sloffer.Jobs.Sources.Jobicy;

/// <summary>Settings for <see cref="JobicyJobSource"/>, bound from <c>JobFetch:Jobicy</c>.</summary>
public sealed class JobicyOptions
{
    public const string SectionName = "JobFetch:Jobicy";

    /// <summary>Jobicy asks clients not to poll more than once per hour.</summary>
    public static readonly TimeSpan MinAllowedInterval = TimeSpan.FromMinutes(60);

    /// <summary>Most queries allowed per fetch.</summary>
    public const int MaxQueries = 5;

    /// <summary>Jobicy accepts a <c>tag</c> (literal text search) of 3-50 characters.</summary>
    public const int MinTagLength = 3;

    public const int MaxTagLength = 50;

    /// <summary>
    /// The general engineering feed, plus the literal <c>.net</c> tag search (Jobicy has no <c>dotnet</c>/<c>csharp</c>
    /// matches) in Europe and anywhere.
    /// </summary>
    public static IReadOnlyList<JobicyQuery> DefaultQueries { get; } =
    [
        new() { Geo = "europe", Industry = "engineering" },
        new() { Geo = "europe", Tag = ".net" },
        new() { Geo = "anywhere", Tag = ".net" },
    ];

    /// <summary>Listings per request, 1-200.</summary>
    public int Count { get; set; } = 100;

    /// <summary>
    /// Queries run one after another on each fetch (at most <see cref="MaxQueries"/>). Null (not configured) uses
    /// <see cref="DefaultQueries"/>. Kept nullable because the config binder appends to a pre-filled list instead of
    /// replacing it.
    /// </summary>
    public JobicyQuery[]? Queries { get; set; }

    public IReadOnlyList<JobicyQuery> EffectiveQueries => Queries is { Length: > 0 } ? Queries : DefaultQueries;

    /// <summary>Pause between two requests of one fetch (0 to 1 minute).</summary>
    public TimeSpan DelayBetweenRequests { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Minimum time between two fetches. Must be at least <see cref="MinAllowedInterval"/>.</summary>
    public TimeSpan MinInterval { get; set; } = MinAllowedInterval;

    /// <summary>HTTP request timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    internal bool IsValid() =>
        Count is >= 1 and <= 200
        && EffectiveQueries.Count <= MaxQueries
        && EffectiveQueries.All(q => q is not null && q.IsValid())
        && DelayBetweenRequests >= TimeSpan.Zero && DelayBetweenRequests <= TimeSpan.FromMinutes(1)
        && MinInterval >= MinAllowedInterval
        && Timeout > TimeSpan.Zero;
}

/// <summary>One Jobicy request. Blank values are omitted from the query string.</summary>
public sealed class JobicyQuery
{
    /// <summary>Region slug (<c>europe</c>, <c>usa</c>, <c>anywhere</c>, ...).</summary>
    public string? Geo { get; set; }

    /// <summary>Industry slug (<c>engineering</c>, ...).</summary>
    public string? Industry { get; set; }

    /// <summary>Literal text search, 3-50 characters (e.g. <c>.net</c>).</summary>
    public string? Tag { get; set; }

    internal string? GeoOrNull => Normalize(Geo);
    internal string? IndustryOrNull => Normalize(Industry);
    internal string? TagOrNull => Normalize(Tag);

    internal bool IsValid() =>
        (GeoOrNull ?? IndustryOrNull ?? TagOrNull) is not null
        && TagOrNull is null or { Length: >= JobicyOptions.MinTagLength and <= JobicyOptions.MaxTagLength };

    public override string ToString() =>
        string.Join("&", new[] { ("geo", GeoOrNull), ("industry", IndustryOrNull), ("tag", TagOrNull) }
            .Where(p => p.Item2 is not null)
            .Select(p => $"{p.Item1}={p.Item2}"));

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
