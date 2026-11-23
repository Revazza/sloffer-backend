# Feature playbook

New behavior described by acceptance criteria.

1. Restate the goal and list the acceptance criteria. Read the code the feature touches before writing anything.
2. Place code per `CLAUDE.md`:
   - API: one controller per resource, request/response `sealed record`s in `Contracts/`, `[ProducesResponseType<T>]` on every response, never return EF entities. The OpenAPI document is the frontend's contract and must be accurate.
   - Data: entity + `IEntityTypeConfiguration<T>` in `Sloffer.Data`, `DbSet<T>` on `SlofferDbContext`, and a new migration for every model change.
   - Jobs: a `BackgroundService` in `Sloffer.Jobs`, registered in `Program.cs`, `SlofferDbContext` through `IServiceScopeFactory` (one scope per run).
3. External HTTP APIs: one typed client interface per website, behind a small source abstraction, with interval, timeout and item limits in config. Respect the site's terms and rate limits; keep source name and original URL on stored data.
4. Config goes in appsettings with safe defaults (no secrets) and is documented in the README.
5. Tests: happy path, edge cases from the criteria, failure paths (timeouts, bad input, empty data). Never call real external APIs from tests; use fixtures or fakes.
