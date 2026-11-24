using System.Text.RegularExpressions;

namespace Sloffer.Jobs.Matching;

/// <summary>
/// Matches a fixed list of terms in text, case-insensitively, on word boundaries: a term never matches inside a
/// longer word ("C#" does not match "ObjC#", "lead" does not match "leading"). Terms that start with a non-letter
/// (".NET") may follow a word ("ASP.NET" contains ".NET"). Each entry may list synonyms as <c>"A|B"</c>.
/// </summary>
internal sealed class TermMatcher
{
    private const string WordChar = @"[\p{L}\p{N}_]";

    private readonly (string Name, Regex Pattern)[] _terms;

    public TermMatcher(IEnumerable<string> entries)
    {
        _terms = entries
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(entry =>
            {
                var synonyms = entry.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var pattern = string.Join("|", synonyms.Select(TermPattern));
                return (synonyms[0], new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            })
            .ToArray();
    }

    /// <summary>Names of the matching entries, in list order.</summary>
    public IReadOnlyList<string> Matches(string text) =>
        _terms.Where(t => t.Pattern.IsMatch(text)).Select(t => t.Name).ToList();

    public string? FirstMatch(string text) =>
        _terms.FirstOrDefault(t => t.Pattern.IsMatch(text)).Name;

    public bool Any(string text) => _terms.Any(t => t.Pattern.IsMatch(text));

    private static string TermPattern(string term)
    {
        var body = Regex.Escape(term).Replace(@"\ ", @"\s+");
        var left = char.IsLetterOrDigit(term[0]) ? $"(?<!{WordChar})" : "";
        return $"(?:{left}{body}(?!{WordChar}))";
    }
}
