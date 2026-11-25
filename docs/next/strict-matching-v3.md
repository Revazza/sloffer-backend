# Strict matching v3, more sources, Viewed (decided with Sandro, 2026-11-25)

Goal: the list shows only **Excellent** jobs (75+) that really fit, **at most 100 a day**, each with its source (small badge + name), and a **Viewed** mark so Sandro never re-applies to a job he already saw.

## 1. Scoring v3 (backend, golden examples first)
- **Listing language:** the listing must be written in English. A listing mostly in German, Dutch, French (or another non-English language) is rejected: `language: listing is in German`. Detect with stop-word counts (config lists per language); skip the check for very short texts.
- **Required spoken language:** a listing that requires German, Dutch or French ("fluent German", "Deutschkenntnisse", "Dutch speaker", ...) is rejected: `language: requires German`. A clause that says it is only a plus ("German is a plus") does not count.
- **Stack:** .NET must be the stack. A title naming another language and no .NET term ("Go Engineer", "Senior Rust Developer") is rejected: `stack: Go`. Description-only mentions of .NET are already capped at 50 (v2). When .NET is core and another language is also a hard requirement ("C# and Python"), keep the job but subtract points per other core language (`also requires Python`), so pure .NET jobs rank first.
- **Location fit:** Excellent needs remote work in EMEA, or relocation / visa support. An on-site or hybrid job without relocation support is capped at 70 (`on-site without relocation support`). Remote-only boards (Jobicy, Remotive, Remote OK) count as remote. "Fully remote", "100% remote", "remote-first" in the description count as remote; "hybrid" does not.
- Bump `JobScorer.Version`; terms and weights in `JobScoring` config.

## 2. Viewed (backend + frontend)
- `Job.ViewedAt` (nullable, migration). `POST /api/jobs/{id}/viewed` marks it (204), `DELETE` unmarks (204), 404 for unknown ids.
- List and detail return `viewedAt`. `GET /api/jobs?hideViewed=true` hides viewed jobs.
- Frontend: opening a job's detail page marks it viewed; the list shows a "Viewed" chip; a "Hide viewed" filter; "Mark as not viewed" on the detail page.

## 3. Sources (backend, one PR each, pattern of `Sources/Jobicy`)
- **Remotive** (`https://remotive.com/api/remote-jobs?category=software-dev`): its text search is fuzzy, so fetch the category and let the scorer filter. Terms: link back to the job URL, name Remotive as the source, call it only a few times a day (MinInterval 6 h).
- **Remote OK** (`https://remoteok.com/api`): ~100 latest jobs; first array element is a legal notice, skip it. Terms: followed link back, name "Remote OK", **never use their logo**. MinInterval 6 h.
- Adzuna (free API key, large EMEA coverage with `what=.net`): the realistic way to reach ~100 matches a day. Blocked until Sandro creates a key.

## 4. Frontend
- Default list: Excellent (minScore 75), last 24 hours, up to 100 jobs on one page, best first; 3 / 7 days still selectable. Minimum-score choices: Excellent 75+, Good 55+, Maybe 35+, Any.
- Score badge colours by band. Each job shows a small coloured monogram badge with the source name (no third-party logos; Remote OK forbids it), on the list and the detail page.
- Footer credits every source with a followed link.
