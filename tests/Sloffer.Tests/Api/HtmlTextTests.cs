using Sloffer.Api.Jobs;

namespace Sloffer.Tests.Api;

public sealed class HtmlTextTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("<p></p>", null)]
    [InlineData("plain text", "plain text")]
    [InlineData("<p>Hello <b>world</b></p>", "Hello world")]
    [InlineData("<ul>\n<li>One</li>\n<li>Two</li>\n</ul>", "One Two")]
    [InlineData("Tom &amp; Jerry &lt;3", "Tom & Jerry <3")]
    [InlineData("<a href=\"https://x.test\">link</a>", "link")]
    public void Excerpt_strips_tags_and_decodes_entities(string? html, string? expected)
    {
        Assert.Equal(expected, HtmlText.Excerpt(html));
    }

    [Fact]
    public void Excerpt_long_text_is_cut_to_300_characters()
    {
        var excerpt = HtmlText.Excerpt("<p>" + new string('x', 500) + "</p>");

        Assert.Equal(300, excerpt?.Length);
    }

    [Fact]
    public void Excerpt_counts_characters_after_stripping_tags()
    {
        var html = string.Concat(Enumerable.Repeat("<span class=\"long-attribute-value\">ab</span>", 200));

        var excerpt = HtmlText.Excerpt(html);

        Assert.NotNull(excerpt);
        Assert.Equal(300, excerpt.Length);
        Assert.DoesNotContain("<", excerpt);
    }
}
