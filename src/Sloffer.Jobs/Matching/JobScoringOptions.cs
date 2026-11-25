namespace Sloffer.Jobs.Matching;

/// <summary>
/// Rules and weights for <see cref="JobScorer"/>, bound from the <c>JobScoring</c> config section.
/// Every term list is matched case-insensitively on word boundaries. An entry may hold synonyms separated by
/// <c>|</c> (for example <c>"SQL Server|MSSQL"</c>); the first one is the name used in reasons.
/// A list left null uses the default in <see cref="JobScoringDefaults"/>; a list set in config replaces it.
/// When you change the rules themselves (not just weights in config), bump <see cref="JobScorer.Version"/>.
/// </summary>
public sealed class JobScoringOptions
{
    public const string SectionName = "JobScoring";

    /// <summary>Rescore all rows with an outdated <c>ScoreVersion</c> once when the Jobs worker starts.</summary>
    public bool RescoreOnStartup { get; set; }

    /// <summary>Rows loaded and saved per rescore batch.</summary>
    public int RescoreBatchSize { get; set; } = 500;

    // Hard rules
    public string[]? DotNetTerms { get; set; }
    public string[]? EmeaLocations { get; set; }
    public string[]? NonEmeaLocations { get; set; }
    public string[]? RemoteTerms { get; set; }
    public string[]? ExcludedSeniorityTerms { get; set; }

    // Soft rules
    /// <summary>Starting score for jobs that pass every hard rule.</summary>
    public int BaseScore { get; set; } = 40;

    public int TitleDotNetPoints { get; set; } = 15;

    public string[]? SeniorTerms { get; set; }
    public int SeniorPoints { get; set; } = 5;
    public string[]? MidTerms { get; set; }
    public int MidPoints { get; set; } = 10;
    public string[]? LeadTerms { get; set; }
    public int LeadPoints { get; set; } = -10;
    public int NoLevelPoints { get; set; } = 5;

    public string[]? PrimaryDomainTerms { get; set; }
    public int PrimaryDomainPoints { get; set; } = 12;
    public string[]? SecondaryDomainTerms { get; set; }
    public int SecondaryDomainPoints { get; set; } = 6;

    public string[]? StackTerms { get; set; }
    public int StackPointsEach { get; set; } = 3;
    public int StackPointsMax { get; set; } = 15;

    public string[]? GapTerms { get; set; }
    public int GapPointsEach { get; set; } = -3;
    /// <summary>Lowest total the gap rule can reach (a negative number).</summary>
    public int GapPointsMin { get; set; } = -12;

    public string[]? VisaRelocationTerms { get; set; }
    public int VisaRelocationPoints { get; set; } = 8;

    public int RemotePoints { get; set; } = 6;

    /// <summary>"N+ years" with N up to this value gives <see cref="FewYearsPoints"/>.</summary>
    public int FewYearsMax { get; set; } = 5;
    public int FewYearsPoints { get; set; } = 5;
    /// <summary>"N+ years" with N at least this value gives <see cref="ManyYearsPoints"/>. Values in between give 0.</summary>
    public int ManyYearsMin { get; set; } = 8;
    public int ManyYearsPoints { get; set; } = -15;

    internal bool IsValid() =>
        RescoreBatchSize > 0
        && BaseScore is >= 0 and <= 100
        && StackPointsMax >= 0
        && GapPointsMin <= 0
        && FewYearsMax < ManyYearsMin;
}

/// <summary>Default term lists for <see cref="JobScoringOptions"/>.</summary>
public static class JobScoringDefaults
{
    public static readonly string[] DotNetTerms = [".NET", "dotnet", "C#", "ASP.NET"];

    public static readonly string[] EmeaLocations =
    [
        "Europe", "European", "EU", "EEA", "EMEA", "CET", "CEST", "Middle East", "Africa",
        "UK", "United Kingdom", "England", "Scotland", "Wales", "Northern Ireland", "Ireland",
        "Germany", "Netherlands", "Belgium", "Luxembourg", "France", "Spain", "Portugal", "Italy", "Greece", "Cyprus", "Malta",
        "Austria", "Switzerland", "Liechtenstein", "Poland", "Czechia", "Czech Republic", "Slovakia", "Hungary", "Romania",
        "Bulgaria", "Croatia", "Slovenia", "Serbia", "Bosnia", "Montenegro", "North Macedonia", "Albania", "Kosovo",
        "Moldova", "Ukraine", "Lithuania", "Latvia", "Estonia", "Finland", "Sweden", "Norway", "Denmark", "Iceland",
        "Georgia", "Armenia", "Azerbaijan", "Turkey", "Türkiye", "Israel", "UAE", "United Arab Emirates", "Dubai",
        "Saudi Arabia", "Qatar", "Bahrain", "Kuwait", "Oman", "Jordan", "Egypt", "Morocco", "Tunisia",
        "South Africa", "Nigeria", "Kenya",
        // Local-language country names, as some boards (Arbeitnow) write them.
        "Deutschland", "Nederland", "België|Belgique", "Schweiz|Suisse|Svizzera", "Österreich", "España", "Italia",
        "Polska", "Česko|Česká republika", "Magyarország", "România", "Sverige", "Norge", "Danmark", "Suomi", "Éire",
        "Hrvatska", "Slovensko", "Slovenija", "Srbija", "Lietuva", "Latvija", "Eesti",
        // Major cities, for listings that give only a city.
        "London", "Manchester", "Edinburgh", "Dublin", "Berlin", "Munich|München", "Hamburg", "Frankfurt",
        "Cologne|Köln", "Düsseldorf", "Stuttgart", "Nuremberg|Nürnberg", "Leipzig", "Dresden", "Hanover|Hannover",
        "Bremen", "Karlsruhe", "Mannheim", "Amsterdam", "Rotterdam", "Utrecht", "The Hague|Den Haag", "Eindhoven",
        "Brussels|Bruxelles|Brussel", "Antwerp|Antwerpen", "Paris", "Lyon", "Toulouse", "Nantes", "Lille", "Madrid",
        "Barcelona", "Lisbon|Lisboa","Milan|Milano", "Rome|Roma", "Turin|Torino", "Vienna|Wien", "Graz",
        "Zurich|Zürich", "Geneva|Genève", "Basel", "Bern", "Lausanne", "Lucerne|Luzern", "Warsaw|Warszawa",
        "Krakow|Kraków", "Wroclaw|Wrocław", "Gdansk|Gdańsk", "Poznan|Poznań", "Prague|Praha", "Brno", "Budapest",
        "Bucharest|București", "Sofia", "Belgrade|Beograd", "Zagreb", "Ljubljana", "Bratislava", "Athens", "Stockholm",
        "Gothenburg|Göteborg", "Malmö", "Oslo", "Copenhagen|København", "Helsinki", "Tallinn", "Riga", "Vilnius",
        "Kyiv|Kiev", "Lviv", "Tbilisi", "Yerevan", "Istanbul", "Tel Aviv", "Cairo", "Cape Town", "Lagos", "Nairobi",
    ];

    public static readonly string[] NonEmeaLocations =
    [
        "US only", "USA", "US", "United States", "Canada", "LATAM", "Latin America", "APAC", "Asia", "North America",
        "Americas", "Australia", "New Zealand", "India", "Brazil", "Mexico", "Argentina",
    ];

    public static readonly string[] RemoteTerms = ["remote", "worldwide", "anywhere"];

    public static readonly string[] ExcludedSeniorityTerms =
        ["junior", "intern", "trainee", "graduate", "staff", "principal", "head of", "director", "VP", "chief"];

    public static readonly string[] SeniorTerms = ["senior"];
    public static readonly string[] MidTerms = ["mid", "medior", "regular"];
    public static readonly string[] LeadTerms = ["lead"];

    public static readonly string[] PrimaryDomainTerms =
        ["fintech", "payments", "banking", "bank", "financial", "trading", "insurance", "open banking"];

    public static readonly string[] SecondaryDomainTerms = ["marketplace", "travel", "booking", "e-commerce|ecommerce"];

    public static readonly string[] StackTerms =
    [
        "microservices", "Kafka", "RabbitMQ", "Redis", "PostgreSQL", "SQL Server|MSSQL", "Elasticsearch", "GraphQL",
        "Keycloak", "MediatR", "EF Core|Entity Framework", "xUnit|NUnit|Testcontainers", "event-driven",
        "distributed systems",
    ];

    public static readonly string[] GapTerms = ["Kubernetes|K8s", "AWS", "Azure", "Terraform", "React|Angular|Vue"];

    public static readonly string[] VisaRelocationTerms =
        ["visa sponsorship", "visa support", "sponsor visa|sponsor your visa", "relocation"];
}
