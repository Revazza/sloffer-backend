# sloffer-backend

.NET 10 + PostgreSQL backend for **Sloffer**, a personal tool that pulls job listings from public (no-auth) sources, uses AI to match them to the owner, and serves them to the [web UI](https://github.com/revazza/sloffer-frontend).

Work arrives as ClickUp tasks (`Agent = Backend`) and is implemented via [sloffer-watcher](https://github.com/revazza/sloffer-watcher).

## Quick start

```bash
dotnet tool restore
scripts/setup-local-db.sh                                             # once
dotnet ef database update --project src/Sloffer.Data
dotnet run --project src/Sloffer.Api                                  # http://localhost:5010/api/health
dotnet test Sloffer.slnx
```

See `CLAUDE.md` for structure, all commands and conventions.

## Jobs API (`Sloffer.Api`)

The full contract is the OpenAPI document at `/openapi/v1.json` (Development only).

- `GET /api/jobs`: recent jobs, best match first. Query: `page` (default 1), `pageSize` (default 20, max 100), `q` (title or company, case-insensitive), `location` (case-insensitive contains), `source` (exact), `postedWithinHours` (default `JobFetch:MaxAgeDays * 24`, so 168; max 720; pass 24 for "new today"), `includeDuplicates` (default false), `hideViewed` (default false), `minScore` (0-100, default 55, the "Good" band: scored jobs below it are hidden; `1` hides only rejected jobs, `0` shows all; unscored jobs are always shown), `sort` (`score` (default): `score` descending with unscored jobs last, then `postedAt` descending, then `id`; `posted`: `postedAt` descending, then `id`). Returns `items`, `page`, `pageSize`, `totalCount`; each item has a plain-text `descriptionExcerpt` (first 300 characters), `duplicateOfId`, `score` (null when not scored yet) and `scoreReasons` (empty when not scored yet). Duplicates (see below) are hidden and not counted unless `includeDuplicates=true`. Out-of-range or unknown values return 400 (validation problem details).
- `GET /api/jobs/{id}`: one job including the full `description` as stored from the source, `score` and `scoreReasons`, or 404.
- `POST /api/jobs/{id}/viewed` marks a job as viewed (keeps the first time), `DELETE /api/jobs/{id}/viewed` unmarks it; both 204, or 404 for an unknown id. List items and the detail return `viewedAt` (null if not viewed); `GET /api/jobs?hideViewed=true` hides viewed jobs. The UI marks a job viewed when its original posting link is clicked (list or detail page), so it is never applied to twice.

## Job fetching (`Sloffer.Jobs`)

`JobFetchService` runs a cycle every `JobFetch:CycleDelay`. Each cycle calls every registered `IJobSource` whose `MinInterval` has elapsed since its last fetch (successful or failed; there are no retries), asks for listings posted within the age window (`JobFetch:MaxAgeDays`, default 7 days), drops anything older, and inserts only listings not already stored (unique per `Source` + `ExternalId`).

| Setting | Default | Meaning |
| --- | --- | --- |
| `JobFetch:BackfillDedupOnStartup` | `false` | On worker startup, compute `DedupKey` / `DuplicateOfId` for rows stored before de-duplication existed (batches of 500, oldest first). Safe to run twice. |
| `JobFetch:MaxAgeDays` | `7` | Age window in days (1-30): sources are asked for listings since now minus this window and older listings are dropped. Jobs already stored are never deleted or changed when they age out. The API reads the same key for its default `postedWithinHours`, so set it for both `Sloffer.Api` and `Sloffer.Jobs` (or use the env var `JobFetch__MaxAgeDays`). |
| `JobFetch:CycleDelay` | `00:05:00` | Pause between cycles. |
| `JobFetch:MinInterval` | `01:00:00` | Minimum time between two fetches of the same source. |
| `JobFetch:Sources:<Name>:MinInterval` | (global) | Per-source override, keyed by `IJobSource.Name`. |

Defaults live in `JobFetchOptions`; override them in `src/Sloffer.Jobs/appsettings*.json` or env vars (`JobFetch__MinInterval`). To add a source, implement `IJobSource` and register it in `src/Sloffer.Jobs/Program.cs`.

### Sources

**Jobicy** (`jobicy`, `Sources/Jobicy`): [public remote-jobs API](https://github.com/Jobicy/remote-jobs-api), no auth, called through the RestEase client `IJobicyApi` (`GET https://jobicy.com/api/v2/remote-jobs`). Each fetch runs every query in `JobFetch:Jobicy:Queries` one after another (no pagination), waiting `DelayBetweenRequests` between requests, merges the results and drops repeats by Jobicy id (first one wins). Jobicy's `tag` is a literal text search: `tag=.net` finds .NET jobs, `dotnet`/`csharp` find nothing. Jobicy has no date filter, so listings are filtered by `pubDate`. Jobicy asks for at most one poll per hour: all queries together run at most once per interval, and the interval is forced to at least `01:00:00`, whatever `JobFetch:Sources:jobicy:MinInterval` says. A failing query (HTTP 429/5xx, timeout, bad body) is logged and skipped and the other queries' results are kept; only if every query fails does the fetch throw (`HttpRequestException` for HTTP errors), so the fetcher logs and skips the source until its next run. No retries. Stored jobs keep `Source = jobicy` and the canonical Jobicy `url` (attribution).

| Setting | Default | Meaning |
| --- | --- | --- |
| `JobFetch:Jobicy:Count` | `100` | Listings per request (1-200). |
| `JobFetch:Jobicy:Queries` | see below | Up to 5 queries, each with optional `Geo` (region slug: `europe`, `usa`, `anywhere`, ...), `Industry` (e.g. `engineering`) and `Tag` (3-50 characters); at least one of them per query, blank values are omitted. Setting it replaces the whole default list. |
| `JobFetch:Jobicy:DelayBetweenRequests` | `00:00:03` | Pause between two queries of one fetch (0 to 1 minute). |
| `JobFetch:Jobicy:MinInterval` | `01:00:00` | Time between fetches; lower values fail at startup. |
| `JobFetch:Jobicy:Timeout` | `00:00:30` | HTTP request timeout. |

Default queries (in `JobicyOptions.DefaultQueries`): `geo=europe&industry=engineering`, `geo=europe&tag=.net`, `geo=anywhere&tag=.net`. Override example (`appsettings*.json`):

```json
"Jobicy": { "Queries": [ { "Geo": "europe", "Tag": ".net" }, { "Geo": "europe", "Industry": "engineering" } ] }
```

**Arbeitnow** (`Arbeitnow`, `Sources/Arbeitnow`): [public job board API](https://www.arbeitnow.com/blog/job-board-api), no auth, RestEase client `IArbeitnowClient` (`GET https://www.arbeitnow.com/api/job-board-api?page=N`, 300 listings per page, roughly newest first, `created_at` in Unix seconds); pages from 1 and stops after a page with no listing in the age window, on the last page or at `JobFetch:Arbeitnow:MaxPages` (default `5`, 1-20), waiting `JobFetch:Arbeitnow:DelayBetweenRequests` (default `00:00:02`) between pages, `JobFetch:Arbeitnow:Timeout` default `00:00:30`; keeps only listings whose title, tags or description contain one of `JobFetch:Arbeitnow:RequiredKeywords` (default `.NET`, `C#`, `dotnet`, `ASP.NET`; whole words, case-insensitive; `[]` disables the filter); remote listings get `Remote` in their location; interval via `JobFetch:Sources:Arbeitnow:MinInterval` (else the global `01:00:00`), no retries.

**Remotive** (`Remotive`, `Sources/Remotive`): [public remote-jobs API](https://remotive.com/api/remote-jobs), no auth, RestEase client `IRemotiveClient`. One request per fetch for `JobFetch:Remotive:Category` (default `software-dev`; Remotive's text search is fuzzy, so the category is fetched whole), keeps only listings matching `JobFetch:Remotive:RequiredKeywords` (default .NET terms; empty list disables). Every Remotive job is remote; the location is where the worker may live. Terms: link back to the Remotive job URL and name Remotive as the source (the UI does both), and call it only a few times a day: `JobFetch:Sources:Remotive:MinInterval` is 6 hours.

**Remote OK** (`Remote OK`, `Sources/RemoteOk`): [public API](https://remoteok.com/api), no auth, RestEase client `IRemoteOkClient`. One request per fetch returns the latest ~100 remote jobs (mostly outside EMEA; the scorer filters); the first array element is a legal notice and is skipped. Keeps only listings matching `JobFetch:RemoteOk:RequiredKeywords` (default .NET terms). An empty location is kept empty (not guessed as worldwide). Terms: link back to the Remote OK job URL with a followed link, name "Remote OK" as the source, and never use the Remote OK logo (the UI shows a text monogram). `JobFetch:Sources:Remote OK:MinInterval` is 6 hours.

## De-duplication (`Sloffer.Jobs/Dedup`)

Within one source the unique index on (`Source`, `ExternalId`) prevents repeats. Across sources and reposts, every new job gets a `DedupKey`: `JobDedup.NormalizeKey(company, title)`, the SHA-256 hex of `company|title` after normalization (lowercase, trimmed, whitespace collapsed, punctuation stripped except `#` and `+`, legal suffixes such as Inc/Ltd/GmbH/B.V. removed from the end of the company, gender markers such as "(m/f/d)" and "remote" removed from the title; level words like junior/senior stay). If an original (a stored job with the same key and no `DuplicateOfId`) was posted within `JobFetch:MaxAgeDays` of the new job, the new job gets `DuplicateOfId` = the earliest such original. Duplicates are still stored and scored; nothing is deleted, and stored jobs are never changed by this logic. Rows from before de-duplication have a null key until the backfill runs (`JobFetch:BackfillDedupOnStartup`).

## Job scoring (`Sloffer.Jobs/Matching`)

`JobScorer` gives every new job a 0-100 fit score with plain-language reasons when it is inserted, using deterministic rules (no AI, no network). The result is stored on the job as `Score`, `ScoreReasons` (jsonb string array), `ScoreVersion` and `ScoredAt`. Rows stored before scoring existed stay null until rescored.

- **Hard rules** (any failure gives `0` with one reason per failed rule): the **title** is a software engineering IC role (no `RoleRejectTitleTerms` word such as qa, manager, director, sales, support, analyst, data engineer, frontend, junior, intern; and at least one `EngineeringTitleTerms` word such as developer, engineer, backend, architect, .NET; reason `role: <term>`); mentions .NET (`.NET`, `dotnet`, `C#`, `ASP.NET`); location in EMEA (English and local country names such as Deutschland or Nederland, plus major EMEA cities for city-only locations) or remote/worldwide/anywhere, and not only non-EMEA regions (an empty location passes only if the title or description says remote); title has no excluded seniority word (staff, principal).
- **Soft rules** (start at `BaseScore` 40, clamp 0-100, one reason each): **.NET centrality**: core (a .NET term in the title, or at least 2 strong mentions in the description) +25; only mentioned (one strong mention, or only in an alternatives list like "Java, Python or C#" / "e.g. Java, C#" / "Swift/Kotlin/C#", next to "a plus"/"a bonus", or after a short "Nice to have" / "Bonus points" / "Preferred" heading) caps the score at 50; seniority fit: senior/sr, mid/middle/medior/regular or no level word +5, lead (team/tech lead) -5, staff/principal -15 (a penalty, not a reject); domain phrases fintech/payments/banking/financial services/insurtech/... +12 or marketplace/online travel/booking/e-commerce +6 (larger only; bare words like "insurance", "bank" and "travel" are not used because they mostly appear in benefits and travel requirements); stack matches +3 each (max +15); gaps (Kubernetes, AWS, Azure, Terraform, React/Angular/Vue) -3 each (max -12); visa sponsorship or relocation +8; remote in EMEA +6; largest "N+ years": N <= 6 +5, 7-9 -8, >= 10 -20.

**Strict matching (v3).** Hard rules also: the listing is written in English (most stop words; `language: listing is in German`), it does not require German, Dutch or French (`language: requires German`, unless the clause says it is only a plus), and the title does not name another language without a .NET term (`stack: Go`). Soft rules also: every other language that is a hard requirement next to core .NET -8 (max -16, `also requires Python`); an on-site or hybrid job without relocation or visa support is capped at 70 (`OnSiteMaxScore`, 100 turns it off). Remote counts when the location or title says remote, the description says fully remote / 100% remote / remote-first, or the source only lists remote jobs (`RemoteOnlySources`: Jobicy, Remotive, Remote OK).

**Score bands** (the UI filters on these): 75-100 Excellent (role gate passed, .NET core, EMEA, seniority and years fit), 55-74 Good, 35-54 Maybe, 1-34 Weak, 0 rejected (a hard rule failed). `GET /api/jobs` shows 55+ by default. A job where .NET is only mentioned is capped at 50, so it never reaches the default list.

Every weight and term list is in `JobScoringOptions` (section `JobScoring`). A term list set in config replaces the default list (`JobScoringDefaults`); entries match case-insensitively on word boundaries (also on the left for terms starting with punctuation, so `.NET` does not match `risk.net` or `ASP.NET`; `ASP.NET` is its own term) and may list synonyms as `"SQL Server|MSSQL"`. When the rules change, bump `JobScorer.Version`.

| Setting | Default | Meaning |
| --- | --- | --- |
| `JobScoring:RescoreOnStartup` | `false` | On worker startup, rescore every row whose `ScoreVersion` is not the current version (including unscored rows), before the first fetch cycle. |
| `JobScoring:RescoreBatchSize` | `500` | Rows per rescore batch. |
| `JobScoring:BaseScore` | `40` | Starting score for jobs that pass the hard rules. |
