namespace Sloffer.Jobs.Sources.Arbeitnow;

/// <summary>Settings for <see cref="ArbeitnowJobSource"/>, bound from <c>JobFetch:Arbeitnow</c>.</summary>
public sealed class ArbeitnowOptions
{
    public const string SectionName = "JobFetch:Arbeitnow";

    public static readonly IReadOnlyList<string> DefaultRequiredKeywords = [".NET", "C#", "dotnet", "ASP.NET"];

    /// <summary>Maximum pages (300 listings each) per fetch, 1-20.</summary>
    public int MaxPages { get; set; } = 5;

    /// <summary>Pause between two page requests of one fetch (0 to 1 minute).</summary>
    public TimeSpan DelayBetweenRequests { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>HTTP request timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A listing is kept only if its title, tags or description contain one of these terms (case-insensitive, whole
    /// words). Null (not configured) uses <see cref="DefaultRequiredKeywords"/>; an empty list (or only blank entries)
    /// disables the filter. Kept nullable because the config binder appends to a pre-filled list instead of replacing it.
    /// </summary>
    public string[]? RequiredKeywords { get; set; }

    public IReadOnlyList<string> EffectiveRequiredKeywords =>
        RequiredKeywords is null
            ? DefaultRequiredKeywords
            : RequiredKeywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList();

    internal bool IsValid() =>
        MaxPages is >= 1 and <= 20
        && DelayBetweenRequests >= TimeSpan.Zero && DelayBetweenRequests <= TimeSpan.FromMinutes(1)
        && Timeout > TimeSpan.Zero;
}
