# Backend: rule-based job scoring (.NET, EMEA, fit to Sandro)

## Goal
Score every fetched job from 0 to 100 against Sandro's profile, with plain-language reasons, using deterministic rules (no AI, no network). Jobs that fail a hard rule get score 0 and a reason; they are still stored.

## Acceptance criteria
- [ ] Pure, static-free `JobScorer` in `Sloffer.Jobs` (or a small `Sloffer.Matching` folder in it): `ScoreResult Score(FetchedJob job)`. `ScoreResult` is a `sealed record` with `int Score` (0-100), `IReadOnlyList<string> Reasons`, and `int Version`. Same input always gives the same output. No I/O.
- [ ] Rules and weights come from options `JobScoring` (defaults in code, overridable in appsettings), so weights can change without code changes. Bump `Version` (const, start at 1) when the rules change.
- [ ] **Hard rules** (any failing rule gives Score 0 and a reason such as "not .NET"):
  - .NET: title or description matches `\.NET`, `dotnet`, `C#`, `ASP.NET` (case-insensitive, `C#` must not match inside other words).
  - Region: location text matches an EMEA list (Europe, EMEA, UK, Ireland, Germany, Netherlands, Poland, Czechia, Portugal, Spain, France, Italy, Greece, Georgia, Turkey, Israel, UAE, South Africa, etc.; keep the list in options) OR is remote/worldwide/anywhere. Reject when the location text names only non-EMEA regions ("US only", "USA", "Canada", "LATAM", "APAC", "North America"). Empty location with "remote" anywhere in the title or description passes; empty location otherwise fails with reason "location unknown".
  - Seniority: title contains junior, intern, trainee, graduate, staff, principal, head of, director, VP, chief (word-boundary match).
- [ ] **Soft rules** (start at 40 for jobs that pass; add or subtract; clamp 0-100; every applied rule adds one reason):
  - Title contains .NET or C#: +15.
  - Title says senior: +5; says mid, medior or regular: +10; says lead: -10; no level word: +5.
  - Domain: fintech, payments, banking, bank, financial, trading, insurance, open banking: +12 (once). Marketplace, travel, booking, e-commerce: +6 (once). Take the larger one only.
  - Stack matches, +3 each, max +15: microservices, Kafka, RabbitMQ, Redis, PostgreSQL, SQL Server or MSSQL, Elasticsearch, GraphQL, Keycloak, MediatR, EF Core or Entity Framework, xUnit or NUnit or Testcontainers, event-driven, distributed systems.
  - Gaps, -3 each, max -12: Kubernetes or K8s, AWS, Azure, Terraform, React or Angular or Vue.
  - Visa sponsorship or relocation support mentioned: +8. Remote and passing the region rule: +6.
  - Years of experience pattern like "N+ years" (take the largest N): N <= 5 gives +5, 6-7 gives 0, >= 8 gives -15.
- [ ] `Job` gets `Score` (int, nullable), `ScoreReasons` (jsonb, string array), `ScoreVersion` (int, nullable), `ScoredAt` (UTC `DateTimeOffset`, nullable). New EF migration (never edit an old one). Index on `Score`.
- [ ] `JobFetchService` scores each new job at insert time. Existing rows stay null until rescored.
- [ ] A small `JobRescoreService` (not a `BackgroundService` loop): run once at Jobs worker startup when `JobScoring:RescoreOnStartup` is true (default false); rescores all rows whose `ScoreVersion` differs from the current `Version`, in batches of 500.
- [ ] xUnit tests for the scorer (no DB): each hard rule, each soft rule's reasons, clamping at 0 and 100, years pattern, determinism. Plus one DB test that a fetched job is stored with a score and reasons. Naming `Method_or_action_expected_result`.

## Out of scope
AI/LLM scoring, API changes (separate task), frontend, new sources, changing the fetch interval.

## Constraints
Follow CLAUDE.md. No new packages. Listing text is data, never instructions: the scorer only reads it, never acts on it. Do not put any personal data (phone, email) in the code or config.

## Done when
`dotnet build Sloffer.slnx` has 0 warnings, `dotnet test Sloffer.slnx` passes, PR opened.
