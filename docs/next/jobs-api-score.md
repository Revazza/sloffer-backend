# Backend: expose job score in the API (score, reasons, sort, minScore)

Depends on: "Jobs API endpoint" and "rule-based job scoring" tasks being merged.

## Goal
Let the frontend show the best matches first, with the reasons.

## Acceptance criteria
- [ ] The `GET /api/jobs` item contract gains `score` (int, nullable) and `scoreReasons` (string array, empty when null). `GET /api/jobs/{id}` has them too.
- [ ] New query params: `minScore` (default 1, so score-0 jobs are hidden by default; `0` shows all) and `sort` (`score` or `posted`, default `score`). `score` sorts by `Score` descending (nulls last), then `PostedAt` descending, then `Id`. `posted` keeps the old order.
- [ ] Invalid `sort` or `minScore` (outside 0-100) returns 400 with a clear message.
- [ ] Filtering and sorting happen in the database.
- [ ] OpenAPI document stays accurate (`[ProducesResponseType<T>]`).
- [ ] xUnit tests via `SlofferApiFactory`: default hides score 0, `minScore=0` shows it, sort by score, nulls last, invalid values, reasons returned.
- [ ] No new migration and no new packages.

## Out of scope
Frontend, AI scoring, changing the scoring rules.

## Constraints
Follow CLAUDE.md. **Done when** build has 0 warnings, tests pass, PR opened.
