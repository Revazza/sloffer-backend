using System.Net;
using System.Text.RegularExpressions;

namespace Sloffer.Api.Jobs;

/// <summary>Turns listing HTML into short plain text for previews.</summary>
public static partial class HtmlText
{
    public const int ExcerptLength = 300;

    /// <summary>Strips HTML tags, decodes entities, collapses whitespace and keeps the first <paramref name="maxLength"/> characters.</summary>
    public static string? Excerpt(string? html, int maxLength = ExcerptLength)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var text = TagRegex().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();

        if (text.Length == 0)
            return null;

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
