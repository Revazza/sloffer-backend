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

## Job fetching (`src/Sloffer.Jobs`)

`JobFetchService` runs a cycle every `CycleDelay`. Each cycle asks every registered `IJobSource` for jobs posted in the last 24h, unless that source was fetched less than its `MinInterval` ago. Older jobs are dropped and jobs already stored (same `Source` + `ExternalId`) are skipped. A failing source is logged and waits for its next allowed run (no retries).

Config section `JobFetch` (defaults are in code; `appsettings.json` may override them):

| Key | Default | Meaning |
| --- | --- | --- |
| `MinInterval` | `01:00:00` | Minimum time between two fetches of one source |
| `CycleDelay` | `00:01:00` | Delay between worker cycles |
| `Sources:<name>:MinInterval` | (none) | Per-source override of `MinInterval`, keyed by `IJobSource.Name` |
