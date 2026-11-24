using Sloffer.Jobs.Dedup;

namespace Sloffer.Tests.Dedup;

public sealed class JobDedupTests
{
    private static readonly string Reference = JobDedup.NormalizeKey("Acme", "Senior .NET Developer");

    [Fact]
    public void NormalizeKey_returns_lowercase_sha256_hex()
    {
        Assert.Matches("^[0-9a-f]{64}$", Reference);
    }

    [Fact]
    public void NormalizeKey_same_input_gives_same_key()
    {
        Assert.Equal(Reference, JobDedup.NormalizeKey("Acme", "Senior .NET Developer"));
    }

    [Theory]
    [InlineData("Acme Inc.")]
    [InlineData("Acme, Inc")]
    [InlineData("Acme Ltd")]
    [InlineData("Acme Limited")]
    [InlineData("Acme LLC")]
    [InlineData("Acme GmbH")]
    [InlineData("Acme AG")]
    [InlineData("Acme B.V.")]
    [InlineData("Acme BV")]
    [InlineData("Acme S.A.")]
    [InlineData("Acme PLC")]
    [InlineData("Acme Corp.")]
    [InlineData("Acme Co")]
    [InlineData("Acme GmbH & Co.")]
    public void NormalizeKey_ignores_legal_suffixes_of_the_company(string company)
    {
        Assert.Equal(Reference, JobDedup.NormalizeKey(company, "Senior .NET Developer"));
    }

    [Theory]
    [InlineData("Senior .NET Developer (m/f/d)")]
    [InlineData("Senior .NET Developer (f/m/x)")]
    [InlineData("Senior .NET Developer (m/w/d)")]
    [InlineData("Senior .NET Developer (M/W/D)")]
    [InlineData("Senior .NET Developer (all genders)")]
    [InlineData("Senior .NET Developer - Remote")]
    [InlineData("Senior .NET Developer (Remote)")]
    [InlineData("Remote Senior .NET Developer")]
    [InlineData("Senior .NET Developer (m/f/d) - Remote")]
    public void NormalizeKey_ignores_gender_markers_and_remote_in_the_title(string title)
    {
        Assert.Equal(Reference, JobDedup.NormalizeKey("Acme", title));
    }

    [Theory]
    [InlineData("ACME", "SENIOR .NET DEVELOPER")]
    [InlineData("  acme  ", "  senior   .net\tdeveloper ")]
    [InlineData("Acme", "Senior .NET-Developer")]
    [InlineData("Acme!", "Senior .NET Developer.")]
    public void NormalizeKey_ignores_case_spacing_and_punctuation(string company, string title)
    {
        Assert.Equal(Reference, JobDedup.NormalizeKey(company, title));
    }

    [Theory]
    [InlineData("Junior .NET Developer")]
    [InlineData("Mid .NET Developer")]
    [InlineData("Lead .NET Developer")]
    [InlineData(".NET Developer")]
    public void NormalizeKey_keeps_level_words_so_levels_are_different_jobs(string title)
    {
        Assert.NotEqual(Reference, JobDedup.NormalizeKey("Acme", title));
    }

    [Fact]
    public void NormalizeKey_different_company_same_title_differs()
    {
        Assert.NotEqual(Reference, JobDedup.NormalizeKey("Globex", "Senior .NET Developer"));
    }

    [Fact]
    public void NormalizeKey_keeps_hash_and_plus_so_csharp_and_cpp_differ()
    {
        Assert.NotEqual(JobDedup.NormalizeKey("Acme", "C# Developer"), JobDedup.NormalizeKey("Acme", "C++ Developer"));
    }

    [Fact]
    public void NormalizeKey_company_that_is_only_a_suffix_word_is_kept()
    {
        Assert.NotEqual(JobDedup.NormalizeKey("Co", "Developer"), JobDedup.NormalizeKey("", "Developer"));
    }

    [Fact]
    public void NormalizeKey_separates_company_and_title()
    {
        Assert.NotEqual(JobDedup.NormalizeKey("Acme Senior", ".NET Developer"), Reference);
    }

    [Fact]
    public void NormalizeKey_remote_inside_a_word_is_kept()
    {
        Assert.NotEqual(
            JobDedup.NormalizeKey("Acme", "Remoteness Engineer"),
            JobDedup.NormalizeKey("Acme", "ness Engineer"));
    }
}
