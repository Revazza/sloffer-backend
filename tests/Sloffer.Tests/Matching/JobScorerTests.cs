using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Matching;

namespace Sloffer.Tests.Matching;

/// <summary>Pure scorer tests: no database, no network.</summary>
public sealed class JobScorerTests
{
    // Passes every hard rule and hits only one soft rule ("no level in title"): 40 + 5.
    private const string BaseTitle = "Backend Developer";
    private const string BaseLocation = "Spain";
    private const string BaseDescription = "We write C#.";
    private const int BaseExpected = 45;

    private static readonly JobScorer Scorer = CreateScorer();

    [Fact]
    public void Score_baseline_job_returns_base_score_plus_no_level()
    {
        var result = Score();

        Assert.Equal(BaseExpected, result.Score);
        Assert.Equal(["no level in title (+5)"], result.Reasons);
        Assert.Equal(JobScorer.Version, result.Version);
        Assert.Equal(2, JobScorer.Version);
    }

    [Fact]
    public void Score_realistic_fintech_dotnet_job_adds_every_matching_rule()
    {
        var result = Score(
            title: "Senior .NET Engineer (Payments)",
            location: "Remote, Europe",
            description: "<p>We build payments microservices with Kafka and PostgreSQL on Azure.</p><p>4+ years of C#. Relocation support.</p>");

        Assert.Equal(
        [
            "title mentions .NET/C# (+15)",
            "senior title (+5)",
            "domain: payments (+12)",
            "stack: microservices, Kafka, PostgreSQL (+9)",
            "gaps: Azure (-3)",
            "visa sponsorship or relocation mentioned (+8)",
            "remote in EMEA (+6)",
            "asks 4+ years (+5)",
        ], result.Reasons);
        Assert.Equal(40 + 15 + 5 + 12 + 9 - 3 + 8 + 6 + 5, result.Score);
    }

    // ---- Hard rule: .NET ----

    [Theory]
    [InlineData("Java Developer", "We write Java and Kotlin.")]
    [InlineData("Go Engineer", "Experience with ObjC# is a plus.")]
    [InlineData("Network Engineer", "Manage example.network domains.")]
    [InlineData("Regulatory Lead", "eFX Trading Platform of the Year (Risk.net).")]
    [InlineData("Fraud Analyst", "<img src=\"https://assets.ctfassets.net/3h69/logo.png\">See https://images.ctfassets.net/x.png")]
    [InlineData("Account Executive", "Read more at aboutus.godaddy.net/about-us")]
    public void Score_job_without_dotnet_returns_0_not_dotnet(string title, string description)
    {
        var result = Score(title: title, description: description);

        Assert.Equal(0, result.Score);
        Assert.Equal(["not .NET"], result.Reasons);
    }

    [Theory]
    [InlineData("Backend Developer", "We use .NET 9.")]
    [InlineData("Backend Developer", "dotnet and friends")]
    [InlineData("Backend Developer", "Strong c# skills.")]
    [InlineData("Backend Developer", "ASP.NET Core APIs")]
    [InlineData("Backend Developer", "Web apps with C# / ASP.NET /.NET.")]
    [InlineData("Backend Developer", "(.NET 8) services")]
    [InlineData("Backend Developer", "Built on .net core.")]
    [InlineData("C# Developer", "")]
    [InlineData("Backend Developer", "<li>C&#35; and SQL</li>")]
    public void Score_dotnet_in_title_or_description_passes(string title, string description)
    {
        Assert.True(Score(title: title, description: description).Score > 0);
    }

    // ---- Hard rule: region ----

    [Theory]
    [InlineData("Spain")]
    [InlineData("London, UK")]
    [InlineData("EMEA")]
    [InlineData("Remote - Europe")]
    [InlineData("Canada,  UK,  USA")]
    [InlineData("Worldwide")]
    [InlineData("Anywhere")]
    [InlineData("Tbilisi, Georgia")]
    [InlineData("Woerden, Utrecht, Nederland")]
    [InlineData("Oberkochen , Baden-Württemberg, Deutschland")]
    [InlineData("Luzern, Luzern, Schweiz")]
    [InlineData("London")]
    [InlineData("Amsterdam")]
    [InlineData("Stuttgart")]
    [InlineData("München")]
    [InlineData("Zürich")]
    [InlineData("Kraków")]
    public void Score_location_in_emea_or_remote_passes(string location)
    {
        Assert.True(Score(location: location).Score > 0);
    }

    [Theory]
    [InlineData("USA", "location outside EMEA (USA)")]
    [InlineData("Remote - US only", "location outside EMEA (US only)")]
    [InlineData("Remote, North America", "location outside EMEA (North America)")]
    [InlineData("LATAM", "location outside EMEA (LATAM)")]
    [InlineData("APAC", "location outside EMEA (APAC)")]
    [InlineData("Canada", "location outside EMEA (Canada)")]
    [InlineData("Singapore", "location not in EMEA (Singapore)")]
    [InlineData("Porto Alegre, Brazil", "location outside EMEA (Brazil)")]
    [InlineData("Berlington", "location not in EMEA (Berlington)")]
    public void Score_location_outside_emea_returns_0_with_reason(string location, string reason)
    {
        var result = Score(location: location);

        Assert.Equal(0, result.Score);
        Assert.Equal([reason], result.Reasons);
    }

    [Fact]
    public void Score_empty_location_with_remote_in_description_passes_as_remote()
    {
        var result = Score(location: "", description: "Fully remote C# role.");

        Assert.Equal(BaseExpected + 6, result.Score);
        Assert.Contains("remote in EMEA (+6)", result.Reasons);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Score_empty_location_without_remote_returns_0_location_unknown(string? location)
    {
        var result = Score(location: location, description: "Office based C# role.");

        Assert.Equal(0, result.Score);
        Assert.Equal(["location unknown"], result.Reasons);
    }

    // ---- Hard rule: seniority ----

    [Theory]
    [InlineData("Junior .NET Developer", "junior")]
    [InlineData(".NET Intern", "intern")]
    [InlineData("Trainee C# Developer", "trainee")]
    [InlineData("Graduate Software Engineer", "graduate")]
    [InlineData("Staff Engineer", "staff")]
    [InlineData("Principal .NET Engineer", "principal")]
    [InlineData("Head of Engineering", "head of")]
    [InlineData("Engineering Director", "director")]
    [InlineData("VP Engineering", "VP")]
    [InlineData("Chief Technology Officer", "chief")]
    public void Score_excluded_seniority_in_title_returns_0_with_reason(string title, string term)
    {
        var result = Score(title: title);

        Assert.Equal(0, result.Score);
        Assert.Equal([$"seniority excluded ({term})"], result.Reasons);
    }

    [Theory]
    [InlineData("Internal Tools Developer")]
    [InlineData("Staffing Platform Developer")]
    [InlineData("Backend Developer (Headless CMS)")]
    public void Score_seniority_words_inside_other_words_do_not_exclude(string title)
    {
        Assert.True(Score(title: title).Score > 0);
    }

    [Fact]
    public void Score_multiple_failed_hard_rules_lists_every_reason()
    {
        var result = Score(title: "Junior Java Developer", location: "USA", description: "Java only.");

        Assert.Equal(0, result.Score);
        Assert.Equal(["not .NET", "location outside EMEA (USA)", "seniority excluded (junior)"], result.Reasons);
    }

    // ---- Soft rules ----

    [Fact]
    public void Score_dotnet_in_title_adds_15()
    {
        var result = Score(title: "Backend .NET Developer");

        Assert.Equal(BaseExpected + 15, result.Score);
        Assert.Contains("title mentions .NET/C# (+15)", result.Reasons);
    }

    [Theory]
    [InlineData("Senior Backend Developer", 40 + 5, "senior title (+5)")]
    [InlineData("Mid Backend Developer", 40 + 10, "mid-level title (+10)")]
    [InlineData("Mid-level Backend Developer", 40 + 10, "mid-level title (+10)")]
    [InlineData("Medior Backend Developer", 40 + 10, "mid-level title (+10)")]
    [InlineData("Regular Backend Developer", 40 + 10, "mid-level title (+10)")]
    [InlineData("Backend Team Lead", 40 - 10, "lead title (-10)")]
    [InlineData("Backend Developer", 40 + 5, "no level in title (+5)")]
    public void Score_level_word_in_title_adjusts_score(string title, int expected, string reason)
    {
        var result = Score(title: title);

        Assert.Equal(expected, result.Score);
        Assert.Equal([reason], result.Reasons);
    }

    [Fact]
    public void Score_senior_lead_title_applies_both_level_rules()
    {
        var result = Score(title: "Senior Tech Lead");

        Assert.Equal(40 + 5 - 10, result.Score);
        Assert.Equal(["senior title (+5)", "lead title (-10)"], result.Reasons);
    }

    [Theory]
    [InlineData("A fintech startup.", 12, "domain: fintech (+12)")]
    [InlineData("Open banking APIs for a bank.", 12, "domain: banking (+12)")]
    [InlineData("Insurance claims platform.", 12, "domain: insurance (+12)")]
    [InlineData("A travel booking marketplace.", 6, "domain: marketplace (+6)")]
    [InlineData("Our e-commerce site.", 6, "domain: e-commerce (+6)")]
    [InlineData("A travel site for a bank.", 12, "domain: bank (+12)")]
    public void Score_domain_adds_larger_bonus_once(string extra, int points, string reason)
    {
        var result = Score(description: $"{BaseDescription} {extra}");

        Assert.Equal(BaseExpected + points, result.Score);
        Assert.Single(result.Reasons, r => r.StartsWith("domain:", StringComparison.Ordinal));
        Assert.Contains(reason, result.Reasons);
    }

    [Fact]
    public void Score_stack_matches_add_3_each()
    {
        var result = Score(description: "C# with Redis, MSSQL and Entity Framework, tested with xUnit.");

        Assert.Equal(BaseExpected + 12, result.Score);
        Assert.Contains("stack: Redis, SQL Server, EF Core, xUnit (+12)", result.Reasons);
    }

    [Fact]
    public void Score_stack_matches_are_capped_at_15()
    {
        var result = Score(description: "C#, microservices, Kafka, RabbitMQ, Redis, PostgreSQL, GraphQL, Keycloak, event-driven.");

        Assert.Equal(BaseExpected + 15, result.Score);
        Assert.Contains("stack: microservices, Kafka, RabbitMQ, Redis, PostgreSQL, GraphQL, Keycloak, event-driven (+15)", result.Reasons);
    }

    [Fact]
    public void Score_gaps_subtract_3_each()
    {
        var result = Score(description: "C# on AWS with Terraform.");

        Assert.Equal(BaseExpected - 6, result.Score);
        Assert.Contains("gaps: AWS, Terraform (-6)", result.Reasons);
    }

    [Fact]
    public void Score_gaps_are_capped_at_minus_12()
    {
        var result = Score(description: "C#, K8s, AWS, Azure, Terraform and Angular.");

        Assert.Equal(BaseExpected - 12, result.Score);
        Assert.Contains("gaps: Kubernetes, AWS, Azure, Terraform, React (-12)", result.Reasons);
    }

    [Theory]
    [InlineData("We offer visa sponsorship.")]
    [InlineData("Relocation package to Berlin.")]
    public void Score_visa_or_relocation_adds_8(string extra)
    {
        var result = Score(description: $"{BaseDescription} {extra}");

        Assert.Equal(BaseExpected + 8, result.Score);
        Assert.Contains("visa sponsorship or relocation mentioned (+8)", result.Reasons);
    }

    [Theory]
    [InlineData("Remote, Europe", BaseTitle)]
    [InlineData("Spain", "Backend Developer (Remote)")]
    public void Score_remote_in_emea_adds_6(string location, string title)
    {
        var result = Score(title: title, location: location);

        Assert.Equal(BaseExpected + 6, result.Score);
        Assert.Contains("remote in EMEA (+6)", result.Reasons);
    }

    [Theory]
    [InlineData("3+ years of C#", 5, "asks 3+ years (+5)")]
    [InlineData("5 + yrs of C#", 5, "asks 5+ years (+5)")]
    [InlineData("6+ years of C#", 0, "asks 6+ years (0)")]
    [InlineData("7+ years of C#", 0, "asks 7+ years (0)")]
    [InlineData("8+ years of C#", -15, "asks 8+ years (-15)")]
    [InlineData("2+ years of SQL and 10+ years of C#", -15, "asks 10+ years (-15)")]
    public void Score_years_pattern_uses_largest_N(string description, int points, string reason)
    {
        var result = Score(description: description);

        Assert.Equal(BaseExpected + points, result.Score);
        Assert.Contains(reason, result.Reasons);
    }

    [Fact]
    public void Score_without_years_pattern_adds_no_years_reason()
    {
        var result = Score(description: "C# with 5 years of history.");

        Assert.DoesNotContain(result.Reasons, r => r.StartsWith("asks", StringComparison.Ordinal));
    }

    // ---- Clamping, determinism, options ----

    [Fact]
    public void Score_is_clamped_at_100()
    {
        var scorer = CreateScorer(new JobScoringOptions { BaseScore = 95 });

        var result = scorer.Score(Job(title: "Mid .NET Developer"));

        Assert.Equal(100, result.Score);
    }

    [Fact]
    public void Score_is_clamped_at_0()
    {
        var scorer = CreateScorer(new JobScoringOptions { BaseScore = 5 });

        var result = scorer.Score(Job(title: "Backend Lead", description: "C# on AWS, Azure and K8s. 10+ years."));

        Assert.Equal(0, result.Score);
        Assert.Contains("lead title (-10)", result.Reasons);
    }

    [Fact]
    public void Score_same_input_gives_same_output()
    {
        var job = Job(title: "Senior .NET Developer", location: "Remote, EMEA",
            description: "Fintech, Kafka, AWS, visa sponsorship, 5+ years.");

        var first = Scorer.Score(job);
        var second = Scorer.Score(job);
        var fromOtherInstance = CreateScorer().Score(job);

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Reasons, second.Reasons);
        Assert.Equal(first.Score, fromOtherInstance.Score);
        Assert.Equal(first.Reasons, fromOtherInstance.Reasons);
    }

    [Fact]
    public void Score_weights_and_lists_come_from_JobScoring_config_and_lists_replace_defaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobScoring:BaseScore"] = "50",
                ["JobScoring:NoLevelPoints"] = "1",
                ["JobScoring:DotNetTerms:0"] = "Java",
            })
            .Build();
        using var services = new ServiceCollection().AddJobScoring(configuration).BuildServiceProvider();
        var scorer = services.GetRequiredService<JobScorer>();

        var java = scorer.Score(Job(title: BaseTitle, description: "We write Java."));
        var dotNet = scorer.Score(Job(title: BaseTitle, description: "We write C#."));

        Assert.Equal(51, java.Score);
        Assert.Equal(["no level in title (+1)"], java.Reasons);
        Assert.Equal(["not .NET"], dotNet.Reasons);
    }

    [Fact]
    public void AddJobScoring_invalid_options_fail_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JobScoring:RescoreBatchSize"] = "0" })
            .Build();
        using var services = new ServiceCollection().AddJobScoring(configuration).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<JobScorer>());
    }

    private static ScoreResult Score(
        string title = BaseTitle,
        string? location = BaseLocation,
        string? description = BaseDescription) =>
        Scorer.Score(Job(title, location, description));

    private static FetchedJob Job(string title = BaseTitle, string? location = BaseLocation, string? description = BaseDescription) =>
        new("test", "1", "https://example.test/1", title, "Example Co", location, "Full-Time", null, description,
            new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));

    private static JobScorer CreateScorer(JobScoringOptions? options = null) =>
        new(Options.Create(options ?? new JobScoringOptions()));
}
