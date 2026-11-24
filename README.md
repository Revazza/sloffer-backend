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

## Job fetching (`Sloffer.Jobs`)

`JobFetchService` runs a cycle every `JobFetch:CycleDelay`. Each cycle calls every registered `IJobSource` whose `MinInterval` has elapsed since its last fetch (successful or failed; there are no retries), asks for listings from the last 24 hours, drops anything older, and inserts only listings not already stored (unique per `Source` + `ExternalId`).

| Setting | Default | Meaning |
| --- | --- | --- |
| `JobFetch:CycleDelay` | `00:05:00` | Pause between cycles. |
| `JobFetch:MinInterval` | `01:00:00` | Minimum time between two fetches of the same source. |
| `JobFetch:Sources:<Name>:MinInterval` | (global) | Per-source override, keyed by `IJobSource.Name`. |

Defaults live in `JobFetchOptions`; override them in `src/Sloffer.Jobs/appsettings*.json` or env vars (`JobFetch__MinInterval`). To add a source, implement `IJobSource` and register it in `src/Sloffer.Jobs/Program.cs`.
