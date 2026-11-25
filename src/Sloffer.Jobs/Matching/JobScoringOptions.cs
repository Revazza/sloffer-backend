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
    /// <summary>Title terms that mean the job is not a software engineering IC role (score 0, "role: term").</summary>
    public string[]? RoleRejectTitleTerms { get; set; }
    /// <summary>Title terms rejected only when the title has no <see cref="EngineeringTitleTerms"/> (e.g. "consultant").</summary>
    public string[]? RoleRejectUnlessEngineeringTitleTerms { get; set; }
    /// <summary>A title must contain one of these to count as an engineering role.</summary>
    public string[]? EngineeringTitleTerms { get; set; }

    /// <summary>Stop words per language. A listing whose words are mostly from a language other than <see cref="ListingLanguage"/> is rejected.</summary>
    public Dictionary<string, string[]>? ListingLanguageStopwords { get; set; }
    /// <summary>The language listings must be written in (a key of <see cref="ListingLanguageStopwords"/>).</summary>
    public string ListingLanguage { get; set; } = "English";
    /// <summary>Texts with fewer words than this skip the listing-language check.</summary>
    public int ListingLanguageMinWords { get; set; } = 25;
    /// <summary>Phrases per spoken language that make it a requirement ("fluent German"); rejected unless the clause says it is only a plus.</summary>
    public Dictionary<string, string[]>? RequiredSpokenLanguageTerms { get; set; }
    /// <summary>A title naming one of these and no .NET term is another stack ("Go Engineer"): rejected.</summary>
    public string[]? TitleOtherLanguageTerms { get; set; }

    public string[]? DotNetTerms { get; set; }
    public string[]? EmeaLocations { get; set; }
    public string[]? NonEmeaLocations { get; set; }
    public string[]? RemoteTerms { get; set; }

    // Soft rules
    /// <summary>Starting score for jobs that pass every hard rule.</summary>
    public int BaseScore { get; set; } = 40;

    // .NET centrality: "core" (title, or enough strong mentions in the description) vs only "mentioned".
    /// <summary>Points when .NET is core to the job.</summary>
    public int DotNetCorePoints { get; set; } = 25;
    /// <summary>Strong description mentions needed for "core" when the title has no .NET term.</summary>
    public int DotNetCoreMinMentions { get; set; } = 2;
    /// <summary>Highest score a job can reach when .NET is only mentioned (keep it below the default list threshold).</summary>
    public int DotNetMentionedMaxScore { get; set; } = 50;
    /// <summary>Other languages: a .NET mention in a clause that offers one of these as an alternative is weak.</summary>
    public string[]? OtherLanguageTerms { get; set; }
    /// <summary>Words that turn a clause into an alternatives list ("Java, Go or .NET", "e.g."). A "/" does too.</summary>
    public string[]? AlternativeListTerms { get; set; }
    /// <summary>"Nice to have" markers: a clause with one is weak, and a short heading line with one makes the rest weak.</summary>
    public string[]? WeakMarkerTerms { get; set; }

    /// <summary>Points per other language that is a hard requirement next to core .NET ("C# and Python").</summary>
    public int OtherCoreLanguagePoints { get; set; } = -8;
    /// <summary>Lowest total of the other-core-language rule (a negative number).</summary>
    public int OtherCoreLanguagePointsMin { get; set; } = -16;

    // Location fit: Excellent needs remote work in EMEA, or relocation / visa support.
    /// <summary>Highest score for an on-site or hybrid job without relocation or visa support. 100 turns the rule off.</summary>
    public int OnSiteMaxScore { get; set; } = 70;
    /// <summary>Sources that only list remote jobs (their location is where the remote worker may live).</summary>
    public string[]? RemoteOnlySources { get; set; }
    /// <summary>Description phrases that make a job remote even when the location is a city.</summary>
    public string[]? RemoteDescriptionTerms { get; set; }
    /// <summary>Title or location words that mean hybrid (not remote).</summary>
    public string[]? HybridTerms { get; set; }

    // Seniority fit (about 4 years, mid to senior): senior, mid and a plain title fit equally well.
    public string[]? SeniorTerms { get; set; }
    public int SeniorPoints { get; set; } = 5;
    public string[]? MidTerms { get; set; }
    public int MidPoints { get; set; } = 5;
    /// <summary>Lead / team lead / tech lead: a small penalty, not a reject.</summary>
    public string[]? LeadTerms { get; set; }
    public int LeadPoints { get; set; } = -5;
    /// <summary>Staff / principal: usually 8+ years, a bigger penalty, not a reject.</summary>
    public string[]? StaffTerms { get; set; }
    public int StaffPoints { get; set; } = -15;
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

    /// <summary>"N+ years" with N up to this value gives <see cref="FewYearsPoints"/> (3-6 years fits).</summary>
    public int FewYearsMax { get; set; } = 6;
    public int FewYearsPoints { get; set; } = 5;
    /// <summary>"N+ years" with N from this value up to <see cref="TooManyYearsMin"/> - 1 gives <see cref="ManyYearsPoints"/>.</summary>
    public int ManyYearsMin { get; set; } = 7;
    public int ManyYearsPoints { get; set; } = -8;
    /// <summary>"N+ years" with N at least this value gives <see cref="TooManyYearsPoints"/>.</summary>
    public int TooManyYearsMin { get; set; } = 10;
    public int TooManyYearsPoints { get; set; } = -20;

    internal bool IsValid() =>
        RescoreBatchSize > 0
        && BaseScore is >= 0 and <= 100
        && StackPointsMax >= 0
        && GapPointsMin <= 0
        && FewYearsMax < ManyYearsMin
        && ManyYearsMin <= TooManyYearsMin
        && DotNetCoreMinMentions >= 1
        && DotNetMentionedMaxScore is >= 0 and <= 100
        && OnSiteMaxScore is >= 0 and <= 100
        && OtherCoreLanguagePointsMin <= 0
        && ListingLanguageMinWords >= 0;
}

/// <summary>Default term lists for <see cref="JobScoringOptions"/>.</summary>
public static class JobScoringDefaults
{
    // The title decides what the job is; a keyword in the description never turns a non-engineering job into a match.
    public static readonly string[] RoleRejectTitleTerms =
    [
        // QA / testing
        "qa", "quality assurance", "test engineer", "tester", "sdet", "test automation", "quality engineer",
        // management ("team lead" / "tech lead" are a seniority penalty, not a reject)
        "manager", "director", "head of", "vp", "vice president", "chief", "cto",
        // non-engineering
        "sales", "account executive", "recruiter", "talent", "marketing", "support", "customer success", "designer",
        "ux", "analyst", "project manager", "product owner", "scrum", "administrator", "technician",
        // data / ML specialisations that are not backend
        "data scientist", "data engineer", "machine learning", "ml engineer",
        // other non-backend specialisations
        "frontend|front-end", "android", "ios",
        // seniority too low
        "intern", "internship", "trainee", "junior", "graduate", "entry level|entry-level", "werkstudent", "praktikum",
        "ausbildung", "duales studium", "stagiaire",
    ];

    public static readonly string[] RoleRejectUnlessEngineeringTitleTerms = ["consultant"];

    public static readonly string[] EngineeringTitleTerms =
    [
        "developer", "engineer", "software", "backend", "back-end", "full stack|full-stack", "fullstack", "architect",
        "programmer", "dev", "swe", ".NET", "C#", "dotnet", "ASP.NET",
        // German and French titles (Arbeitnow)
        "entwickler", "softwareentwickler", "softwareingenieur", "ingenieur", "développeur|developpeur",
        "ingénieur",
    ];

    public static readonly string[] DotNetTerms = [".NET", "dotnet", "C#", "ASP.NET"];

    // Distinctive stop words only: words shared between languages ("in", "die", "als") are left out.
    public static readonly Dictionary<string, string[]> ListingLanguageStopwords = new()
    {
        ["English"] =
            ["the", "and", "you", "with", "for", "our", "we", "will", "are", "to", "of", "is", "your", "on", "as", "be", "this", "or", "us", "have"],
        ["German"] =
            ["und", "der", "das", "wir", "mit", "für", "sie", "ist", "ein", "eine", "einen", "bei", "auf", "oder", "zu", "von", "du", "dich", "dir", "dein", "deine", "unser", "unsere", "uns", "ihr", "ihre", "nicht", "auch", "sind", "werden", "wird", "im", "zum", "zur", "über", "sowie"],
        ["Dutch"] =
            ["het", "een", "van", "wij", "jouw", "jij", "je", "voor", "bij", "naar", "onze", "ook", "niet", "wordt", "worden", "zijn", "en", "met"],
        ["French"] =
            ["et", "le", "la", "les", "des", "nous", "vous", "pour", "avec", "une", "du", "sur", "est", "dans", "au", "aux", "votre", "vos", "notre", "nos", "qui", "que"],
    };

    public static readonly Dictionary<string, string[]> RequiredSpokenLanguageTerms = new()
    {
        ["German"] =
        [
            "fluent German", "fluency in German", "German speaker", "German-speaking", "German speaking",
            "German language skills", "good German", "business German", "native German", "German (C1", "German (B2",
            "German C1", "German B2", "German is required", "German is a must", "Deutschkenntnisse", "fließend Deutsch",
            "fließendes Deutsch", "Deutsch fließend", "verhandlungssicheres Deutsch",
        ],
        ["Dutch"] =
        [
            "fluent Dutch", "fluency in Dutch", "Dutch speaker", "Dutch-speaking", "Dutch speaking", "Dutch language skills",
            "native Dutch", "Dutch is required", "Dutch is a must", "Nederlandstalig", "vloeiend Nederlands",
        ],
        ["French"] =
        [
            "fluent French", "fluency in French", "French speaker", "French-speaking", "French speaking", "native French",
            "French is required", "French is a must", "français courant",
        ],
    };

    public static readonly string[] TitleOtherLanguageTerms =
    [
        "Go|Golang", "Rust", "Java", "Python", "Ruby|Rails", "PHP", "Scala", "Kotlin", "Elixir", "C++", "Node.js|NodeJS|Node",
        "Swift", "Perl", "Clojure", "Haskell", "Erlang",
    ];

    public static readonly string[] RemoteOnlySources = ["jobicy", "Remotive", "Remote OK"];

    public static readonly string[] RemoteDescriptionTerms =
        ["fully remote|fully-remote", "100% remote|100 % remote", "remote-first|remote first", "work from anywhere", "remote within"];

    public static readonly string[] HybridTerms = ["hybrid"];

    // "Go" is left out on purpose: it is also an English verb. "C" alone would match inside "C#".
    public static readonly string[] OtherLanguageTerms =
    [
        "Java", "Golang", "Python", "Ruby", "PHP", "Kotlin", "Scala", "Swift", "C++", "Rust", "Node.js|NodeJS", "Elixir",
        "Perl", "Objective-C", "Dart", "Clojure", "Haskell", "Erlang",
    ];

    public static readonly string[] AlternativeListTerms = ["or", "oder", "ou", "e.g.", "such as", "any of", "one of"];

    public static readonly string[] WeakMarkerTerms =
    [
        "nice to have|nice-to-have", "good to have", "a plus", "bonus", "preferred", "desirable", "an advantage",
        "advantageous", "optional", "von Vorteil", "wünschenswert", "Pluspunkt",
    ];

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

    public static readonly string[] StaffTerms =
        ["staff", "principal"];

    public static readonly string[] SeniorTerms = ["senior", "sr"];
    public static readonly string[] MidTerms = ["mid", "middle", "medior", "regular"];
    public static readonly string[] LeadTerms = ["lead"];

    // Phrases about the employer's business, not bare words: listings say "health insurance", "bank holidays" and
    // "ability to travel" in their benefits and logistics, which said nothing about the domain.
    public static readonly string[] PrimaryDomainTerms =
    [
        "fintech", "payments", "banking", "neobank", "digital bank", "challenger bank", "bank accounts",
        "financial services", "financial institution|financial institutions", "financial markets", "financial crime",
        "financial technology", "financial products", "trading", "insurtech", "insurer|insurers", "insurance company",
        "insurance companies", "insurance platform", "insurance industry", "insurance products", "insurance claims",
    ];

    public static readonly string[] SecondaryDomainTerms =
    [
        "marketplace", "travel tech|traveltech", "travel industry", "travel platform", "online travel", "travel company",
        "booking", "e-commerce|ecommerce",
    ];

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
