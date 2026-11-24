using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sloffer.Jobs.Dedup;

/// <summary>
/// The de-duplication key: SHA-256 hex of <c>company|title</c> after normalization. Pure: same input, same key.
/// Listing text is only read, never acted on.
/// </summary>
public static partial class JobDedup
{
    private static readonly HashSet<string> LegalSuffixes =
        ["inc", "ltd", "limited", "llc", "gmbh", "ag", "bv", "sa", "plc", "corp", "co"];

    /// <summary>
    /// Lowercases, trims and collapses whitespace in both parts; strips punctuation (keeping <c>#</c> and <c>+</c>, so
    /// "C#" and "C++" stay different); removes legal suffixes from the end of the company; removes gender markers
    /// ("(m/f/d)", "(all genders)", ...) and "remote" from the title. Level words (junior, senior, ...) are kept.
    /// </summary>
    public static string NormalizeKey(string? company, string? title)
    {
        var normalized = $"{NormalizeCompany(company)}|{NormalizeTitle(title)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    internal static string NormalizeCompany(string? company)
    {
        var words = Words(company);

        // Drop trailing legal suffixes: "Acme Ltd", "Acme GmbH & Co" and "Acme B.V." all become "acme". Never drop the last word.
        while (words.Count > 1 && LegalSuffixes.Contains(words[^1]))
            words.RemoveAt(words.Count - 1);

        return string.Join(' ', words);
    }

    internal static string NormalizeTitle(string? title)
    {
        var text = (title ?? "").ToLowerInvariant();
        text = GenderMarkerRegex().Replace(text, " ");
        text = RemoteRegex().Replace(text, " ");
        return string.Join(' ', Words(text));
    }

    private static List<string> Words(string? text)
    {
        var lower = (text ?? "").ToLowerInvariant();
        // "B.V." / "S.A." become "bv" / "sa" before punctuation is stripped.
        lower = DottedInitialsRegex().Replace(lower, m => m.Value.Replace(".", "", StringComparison.Ordinal));
        var cleaned = PunctuationRegex().Replace(lower, " ");
        return cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>"(m/f/d)", "(f/m/x)", "(m/w/d)", "(w/m/d)", "(all genders)", "(all gender)".</summary>
    [GeneratedRegex(@"\(\s*(?:[a-z]\s*(?:/\s*[a-z]\s*)+|all\s+genders?)\)", RegexOptions.CultureInvariant)]
    private static partial Regex GenderMarkerRegex();

    /// <summary>"remote" as a word, including "- remote" and "(remote)".</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}_])remote(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant)]
    private static partial Regex RemoteRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:\p{L}\.){2,}")]
    private static partial Regex DottedInitialsRegex();

    /// <summary>Everything except letters, digits, whitespace, '#' and '+'.</summary>
    [GeneratedRegex(@"[^\p{L}\p{N}\s#+]")]
    private static partial Regex PunctuationRegex();
}
