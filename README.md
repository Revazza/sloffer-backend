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

- `GET /api/jobs`: recent jobs, newest first (`postedAt` descending, then `id`). Query: `page` (default 1), `pageSize` (default 20, max 100), `q` (title or company, case-insensitive), `location` (case-insensitive contains), `source` (exact), `postedWithinHours` (default 24, max 24). Returns `items`, `page`, `pageSize`, `totalCount`; each item has a plain-text `descriptionExcerpt` (first 300 characters). Out-of-range values return 400 (validation problem details).
- `GET /api/jobs/{id}`: one job including the full `description` as stored from the source, or 404.

## Job fetching (`Sloffer.Jobs`)

`JobFetchService` runs a cycle every `JobFetch:CycleDelay`. Each cycle calls every registered `IJobSource` whose `MinInterval` has elapsed since its last fetch (successful or failed; there are no retries), asks for listings from the last 24 hours, drops anything older, and inserts only listings not already stored (unique per `Source` + `ExternalId`).

| Setting | Default | Meaning |
| --- | --- | --- |
| `JobFetch:CycleDelay` | `00:05:00` | Pause between cycles. |
| `JobFetch:MinInterval` | `01:00:00` | Minimum time between two fetches of the same source. |
| `JobFetch:Sources:<Name>:MinInterval` | (global) | Per-source override, keyed by `IJobSource.Name`. |

Defaults live in `JobFetchOptions`; override them in `src/Sloffer.Jobs/appsettings*.json` or env vars (`JobFetch__MinInterval`). To add a source, implement `IJobSource` and register it in `src/Sloffer.Jobs/Program.cs`.

### Sources

**Jobicy** (`jobicy`, `Sources/Jobicy`): [public remote-jobs API](https://github.com/Jobicy/remote-jobs-api), no auth, called through the RestEase client `IJobicyApi` (`GET https://jobicy.com/api/v2/remote-jobs`). One request per fetch, no pagination. Jobicy has no date filter, so listings are filtered by `pubDate`. Jobicy asks for at most one poll per hour: the source's interval is forced to at least `01:00:00`, whatever `JobFetch:Sources:jobicy:MinInterval` says. HTTP errors (429, 5xx) throw `HttpRequestException`, so the fetcher logs and skips the source until its next run. Stored jobs keep `Source = jobicy` and the canonical Jobicy `url` (attribution).

| Setting | Default | Meaning |
| --- | --- | --- |
| `JobFetch:Jobicy:Count` | `100` | Listings per request (1-200). |
| `JobFetch:Jobicy:Geo` | `europe` | Region filter (`europe`, `usa`, `anywhere`, ...); empty = none. |
| `JobFetch:Jobicy:Industry` | `engineering` | Industry filter; empty = none. |
| `JobFetch:Jobicy:MinInterval` | `01:00:00` | Time between fetches; lower values fail at startup. |
| `JobFetch:Jobicy:Timeout` | `00:00:30` | HTTP request timeout. |
