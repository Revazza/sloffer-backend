using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Matching;

namespace Sloffer.Tests.Matching;

/// <summary>Pure scorer tests: no database, no network.</summary>
public sealed class JobScorerTests
{
    // Passes every hard rule; .NET is core (2 strong mentions) and the title has no level word: 40 + 25 + 5.
    private const string BaseTitle = "Backend Developer";
    private const string BaseLocation = "Spain";
    private const string BaseDescription = "We write C# on .NET.";
    private const int BaseExpected = 70;
    private const string CoreReason = ".NET core: 2 mentions in the requirements (+25)";

    private static readonly JobScorer Scorer = CreateScorer();

    [Fact]
    public void Score_baseline_job_returns_base_score_plus_no_level()
    {
        var result = Score();

        Assert.Equal(BaseExpected, result.Score);
        Assert.Equal([CoreReason, "no level in title (+5)"], result.Reasons);
        Assert.Equal(JobScorer.Version, result.Version);
        Assert.Equal(5, JobScorer.Version);
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
            ".NET core: in title (+25)",
            "senior title (+5)",
            "domain: payments (+12)",
            "stack: microservices, Kafka, PostgreSQL (+9)",
            "gaps: Azure (-3)",
            "visa sponsorship or relocation mentioned (+8)",
            "remote in EMEA (+6)",
            "asks 4+ years (+5)",
        ], result.Reasons);
        Assert.Equal(100, result.Score); // 40 + 25 + 5 + 12 + 9 - 3 + 8 + 6 + 5 = 107, clamped
    }

    // ---- Hard rule: .NET ----

    [Theory]
    [InlineData("Java Developer", "We write Java and Kotlin.")]
    [InlineData("Go Engineer", "Experience with ObjC# is a plus.")]
    [InlineData("Network Engineer", "Manage example.network domains.")]
    [InlineData("Backend Engineer", "eFX Trading Platform of the Year (Risk.net).")]
    [InlineData("Backend Engineer", "<img src=\"https://assets.ctfassets.net/3h69/logo.png\">See https://images.ctfassets.net/x.png")]
    [InlineData("Backend Engineer", "Read more at aboutus.godaddy.net/about-us")]
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
        var result = Score(location: "", description: "Fully remote C# role on .NET.");

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

    // ---- Hard rule: role (title only) ----

    [Theory]
    [InlineData("Junior .NET Developer", "junior")]
    [InlineData(".NET Intern", "intern")]
    [InlineData("Trainee C# Developer", "trainee")]
    [InlineData("Graduate Software Engineer", "graduate")]
    [InlineData("Head of Engineering", "head of")]
    [InlineData("Engineering Director", "director")]
    [InlineData("VP Engineering", "vp")]
    [InlineData("Chief Technology Officer", "chief")]
    [InlineData("Senior Manager, Engineering Operations", "manager")]
    [InlineData("Engineering Manager (.NET)", "manager")]
    [InlineData("Senior QA Engineer (C#, Selenium)", "qa")]
    [InlineData("SDET - C#", "sdet")]
    [InlineData("Test Automation Engineer .NET", "test automation")]
    [InlineData("Senior Advanced Support Engineer", "support")]
    [InlineData("Sales Engineer", "sales")]
    [InlineData("Enterprise Account Executive - EU", "account executive")]
    [InlineData("Data Engineer (C# a plus)", "data engineer")]
    [InlineData("AI & Data Engineer", "data engineer")]
    [InlineData("Machine Learning Engineer", "machine learning")]
    [InlineData("Senior Frontend Engineer", "frontend")]
    [InlineData("Business Analyst .NET", "analyst")]
    [InlineData("Ausbildung: Fachinformatiker (m/w/d)", "ausbildung")]
    public void Score_non_engineering_or_too_junior_title_returns_0_with_role_reason(string title, string term)
    {
        var result = Score(title: title);

        Assert.Equal(0, result.Score);
        Assert.Equal([$"role: {term}"], result.Reasons);
    }

    [Theory]
    [InlineData("Regulatory Lead")]
    [InlineData("Fachinformatiker Systemintegration")]
    [InlineData("Engineering - Internal AI Transformation")]
    public void Score_title_without_engineering_word_returns_0_not_engineering_role(string title)
    {
        var result = Score(title: title);

        Assert.Equal(0, result.Score);
        Assert.Equal(["role: title is not an engineering role"], result.Reasons);
    }

    [Theory]
    [InlineData("IT Consultant", "role: consultant")]
    [InlineData(".NET Consultant", null)]
    [InlineData("Consultant Software Developer", null)]
    public void Score_consultant_passes_only_with_an_engineering_word(string title, string? reason)
    {
        var result = Score(title: title);

        if (reason is null)
            Assert.True(result.Score > 0);
        else
            Assert.Equal([reason], result.Reasons);
    }

    [Theory]
    [InlineData("Backend Developer")]
    [InlineData(".NET Developer")]
    [InlineData("C# Programmer")]
    [InlineData("Senior Softwareentwickler (m/w/d) .NET")]
    [InlineData("Entwickler C#")]
    [InlineData("Développeur C# .net Front-Office")]
    [InlineData("Software Architect")]
    [InlineData("Fullstack Engineer C# / React")]
    [InlineData("Backend Team Lead")]
    [InlineData("Senior Product Engineer")]
    [InlineData("Supportive Platform Developer")]
    public void Score_engineering_titles_pass_the_role_gate(string title)
    {
        Assert.DoesNotContain(Score(title: title).Reasons, r => r.StartsWith("role:", StringComparison.Ordinal));
    }

    // ---- Hard rule: seniority ----

    [Theory]
    [InlineData("Staff Engineer", "staff")]
    [InlineData("Principal .NET Engineer", "principal")]
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
        Assert.Equal(["role: junior", "not .NET", "location outside EMEA (USA)"], result.Reasons);
    }

    // ---- Soft rules ----

    // ---- .NET centrality ----

    [Fact]
    public void Score_dotnet_in_title_is_core()
    {
        var result = Score(title: "Backend .NET Developer", description: "We build APIs.");

        Assert.Equal(BaseExpected, result.Score);
        Assert.Equal([".NET core: in title (+25)", "no level in title (+5)"], result.Reasons);
    }

    [Theory]
    [InlineData("<h3>Requirements</h3><ul><li>C# and ASP.NET Core</li><li>PostgreSQL</li></ul>")]
    [InlineData("<p>You know C#. You have shipped .NET services.</p>")]
    [InlineData("<ul><li>C#/.NET 8</li></ul>")]
    [InlineData("<p>C# and Python are both used daily on our .NET platform.</p>")]
    public void Score_two_strong_description_mentions_are_core(string description)
    {
        var result = Score(description: description);

        Assert.Contains(CoreReason, result.Reasons);
    }

    [Theory]
    [InlineData("<p>We write C#.</p>", "1 strong mention in the description")]
    [InlineData("<p>Java, Python or C#.</p><p>Any of Java, Kotlin or .NET.</p>", "as an alternative or nice to have")]
    [InlineData("<p>Experience with languages (e.g. C/C++, Java, C#).</p><p>Ruby/C#/.NET welcome.</p>", "as an alternative or nice to have")]
    [InlineData("<p>Java and Spring.</p><p>C# is a plus. .NET would be a bonus.</p>", "as an alternative or nice to have")]
    [InlineData("<h3>Requirements</h3><ul><li>Java</li></ul><h3>Nice to have</h3><ul><li>C#</li><li>.NET</li></ul>", "as an alternative or nice to have")]
    [InlineData("<ul><li>Java</li></ul><p>Bonus points if you have:</p><ul><li>C#</li><li>ASP.NET</li></ul>", "as an alternative or nice to have")]
    [InlineData("<ul><li>Java</li></ul><h3>Preferred qualifications</h3><ul><li>C# and .NET</li></ul>", "as an alternative or nice to have")]
    public void Score_weak_mentions_only_cap_the_score_at_50(string description, string why)
    {
        var result = Score(
            title: "Senior Backend Engineer",
            location: "Remote, Europe",
            description: $"{description}<p>Payments fintech. Kafka, PostgreSQL, Redis. Visa sponsorship.</p>");

        Assert.Equal(50, result.Score);
        Assert.Equal($".NET only mentioned: {why}; score capped at 50", result.Reasons[0]);
    }

    [Fact]
    public void Score_dotnet_as_alternative_in_title_is_not_core()
    {
        var result = Score(title: "Java Developer (or .NET)", description: "<p>Java 21 and Spring Boot.</p>");

        Assert.Equal(".NET only mentioned: as an alternative in the title; score capped at 50", result.Reasons[0]);
    }

    [Fact]
    public void Score_requirement_line_with_a_plus_does_not_make_the_rest_weak()
    {
        var result = Score(description: "<ul><li>Docker is a plus</li><li>C# and ASP.NET Core</li></ul>");

        Assert.Contains(CoreReason, result.Reasons);
    }

    [Fact]
    public void Score_centrality_weights_come_from_config()
    {
        var scorer = CreateScorer(new JobScoringOptions { DotNetCorePoints = 10, DotNetMentionedMaxScore = 30, DotNetCoreMinMentions = 3 });

        var core = scorer.Score(Job(description: "<p>C#, .NET and ASP.NET.</p>"));
        var mentioned = scorer.Score(Job(title: "Senior Backend Developer", description: "<p>C# on .NET. Fintech.</p>"));

        Assert.Equal(40 + 10 + 5, core.Score);
        Assert.Equal(".NET core: 3 mentions in the requirements (+10)", core.Reasons[0]);
        Assert.Equal(30, mentioned.Score);
    }

    [Theory]
    [InlineData("Senior Backend Developer", 40 + 25 + 5, "senior title (+5)")]
    [InlineData("Mid Backend Developer", 40 + 25 + 10, "mid-level title (+10)")]
    [InlineData("Mid-level Backend Developer", 40 + 25 + 10, "mid-level title (+10)")]
    [InlineData("Medior Backend Developer", 40 + 25 + 10, "mid-level title (+10)")]
    [InlineData("Regular Backend Developer", 40 + 25 + 10, "mid-level title (+10)")]
    [InlineData("Backend Team Lead", 40 + 25 - 10, "lead title (-10)")]
    [InlineData("Backend Developer", 40 + 25 + 5, "no level in title (+5)")]
    public void Score_level_word_in_title_adjusts_score(string title, int expected, string reason)
    {
        var result = Score(title: title);

        Assert.Equal(expected, result.Score);
        Assert.Equal([CoreReason, reason], result.Reasons);
    }

    [Fact]
    public void Score_senior_lead_title_applies_both_level_rules()
    {
        var result = Score(title: "Senior Tech Lead Engineer");

        Assert.Equal(40 + 25 + 5 - 10, result.Score);
        Assert.Equal([CoreReason, "senior title (+5)", "lead title (-10)"], result.Reasons);
    }

    [Theory]
    [InlineData("A fintech startup.", 12, "domain: fintech (+12)")]
    [InlineData("Open banking APIs for a bank.", 12, "domain: banking (+12)")]
    [InlineData("Insurance claims platform.", 12, "domain: insurance claims (+12)")]
    [InlineData("A travel booking marketplace.", 6, "domain: marketplace (+6)")]
    [InlineData("Our e-commerce site.", 6, "domain: e-commerce (+6)")]
    [InlineData("A travel platform for a digital bank.", 12, "domain: digital bank (+12)")]
    [InlineData("We fight financial crime.", 12, "domain: financial crime (+12)")]
    [InlineData("Personal and business bank accounts.", 12, "domain: bank accounts (+12)")]
    [InlineData("The leading online travel agency.", 6, "domain: online travel (+6)")]
    public void Score_domain_adds_larger_bonus_once(string extra, int points, string reason)
    {
        var result = Score(description: $"{BaseDescription} {extra}");

        Assert.Equal(BaseExpected + points, result.Score);
        Assert.Single(result.Reasons, r => r.StartsWith("domain:", StringComparison.Ordinal));
        Assert.Contains(reason, result.Reasons);
    }

    [Theory]
    [InlineData("Private health insurance, life insurance and dental insurance.")]
    [InlineData("25 days leave + 8 bank holidays.")]
    [InlineData("Ability to travel up to 25%. Expect to travel twice a year.")]
    [InlineData("Financial wellbeing support and a financial bonus.")]
    public void Score_benefits_and_logistics_wording_adds_no_domain_bonus(string extra)
    {
        var result = Score(description: $"{BaseDescription} {extra}");

        Assert.Equal(BaseExpected, result.Score);
        Assert.DoesNotContain(result.Reasons, r => r.StartsWith("domain:", StringComparison.Ordinal));
    }

    [Fact]
    public void Score_stack_matches_add_3_each()
    {
        var result = Score(description: ".NET and C# with Redis, MSSQL and Entity Framework, tested with xUnit.");

        Assert.Equal(BaseExpected + 12, result.Score);
        Assert.Contains("stack: Redis, SQL Server, EF Core, xUnit (+12)", result.Reasons);
    }

    [Fact]
    public void Score_stack_matches_are_capped_at_15()
    {
        var result = Score(description: ".NET, C#, microservices, Kafka, RabbitMQ, Redis, PostgreSQL, GraphQL, Keycloak, event-driven.");

        Assert.Equal(BaseExpected + 15, result.Score);
        Assert.Contains("stack: microservices, Kafka, RabbitMQ, Redis, PostgreSQL, GraphQL, Keycloak, event-driven (+15)", result.Reasons);
    }

    [Fact]
    public void Score_gaps_subtract_3_each()
    {
        var result = Score(description: ".NET and C# on AWS with Terraform.");

        Assert.Equal(BaseExpected - 6, result.Score);
        Assert.Contains("gaps: AWS, Terraform (-6)", result.Reasons);
    }

    [Fact]
    public void Score_gaps_are_capped_at_minus_12()
    {
        var result = Score(description: ".NET, C#, K8s, AWS, Azure, Terraform and Angular.");

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
        var result = Score(description: $"{BaseDescription} {description}");

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

        var java = scorer.Score(Job(title: BaseTitle, description: "We write Java on the Java platform."));
        var dotNet = scorer.Score(Job(title: BaseTitle, description: "We write C#."));

        Assert.Equal(50 + 25 + 1, java.Score);
        Assert.Equal([".NET core: 2 mentions in the requirements (+25)", "no level in title (+1)"], java.Reasons);
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
