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
