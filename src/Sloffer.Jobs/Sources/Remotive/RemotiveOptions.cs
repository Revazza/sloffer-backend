namespace Sloffer.Jobs.Sources.Remotive;

/// <summary>Settings for <see cref="RemotiveJobSource"/>, bound from <c>JobFetch:Remotive</c>.</summary>
public sealed class RemotiveOptions
{
    public const string SectionName = "JobFetch:Remotive";

    public static readonly IReadOnlyList<string> DefaultRequiredKeywords = [".NET", "C#", "dotnet", "ASP.NET"];

    /// <summary>Remotive category to fetch. Its text search is fuzzy, so the whole category is fetched and filtered here.</summary>
    public string Category { get; set; } = "software-dev";

    /// <summary>HTTP request timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A listing is kept only if its title, tags or description contain one of these terms (case-insensitive, whole
    /// words). Null uses <see cref="DefaultRequiredKeywords"/>; an empty list disables the filter.
    /// </summary>
    public string[]? RequiredKeywords { get; set; }

    public IReadOnlyList<string> EffectiveRequiredKeywords =>
        RequiredKeywords is null
            ? DefaultRequiredKeywords
            : RequiredKeywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList();

    internal bool IsValid() => !string.IsNullOrWhiteSpace(Category) && Timeout > TimeSpan.Zero;
}
