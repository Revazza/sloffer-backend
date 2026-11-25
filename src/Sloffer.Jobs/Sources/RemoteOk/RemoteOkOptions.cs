namespace Sloffer.Jobs.Sources.RemoteOk;

/// <summary>Settings for <see cref="RemoteOkJobSource"/>, bound from <c>JobFetch:RemoteOk</c>.</summary>
public sealed class RemoteOkOptions
{
    public const string SectionName = "JobFetch:RemoteOk";

    public static readonly IReadOnlyList<string> DefaultRequiredKeywords = [".NET", "C#", "dotnet", "ASP.NET"];

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

    internal bool IsValid() => Timeout > TimeSpan.Zero;
}
