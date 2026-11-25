# Sloffer roadmap (autopilot order)

Statuses: `todo`, `doing`, `done`, `blocked`, `later`. Update this file as you go. Specs are in `docs/next/`.

Goal of the product: find .NET-oriented jobs in EMEA that fit Sandro (see Product context in CLAUDE.md), rank them with rules (score + reasons), show them in a web UI.

| # | Item | Spec | Status |
|---|------|------|--------|
| 1 | Check what is already on main: is `score` and `scoreReasons` in `GET /api/jobs`? If not, do `jobs-api-score.md`. Mark `done` when the API has them. | `docs/next/jobs-api-score.md` | todo |
| 2 | Arbeitnow job source | `docs/next/arbeitnow-source.md` | todo |
| 3 | Jobicy extra .NET queries (needs item 2 merged first, both touch README/CLAUDE.md) | `docs/next/jobicy-queries.md` | todo |
| 4 | Real-data check: apply migrations, run `Sloffer.Jobs`, look at the 20 best and 20 rejected jobs. Fix obvious scoring mistakes (wrong rejections, silly rankings) in small PRs with tests. Record findings in `docs/DECISIONS.md`. | none: use judgment | todo |
| 5 | Frontend MVP in `sloffer-frontend` | `docs/next/frontend-mvp.md` | todo |
| 6 | AI scoring of the top N jobs per day | none | later (do not start without asking Sandro) |
| 7 | Himalayas source (needs a free API key from Sandro) | none | blocked: needs key |
| 8 | Per-job status in the UI (new, interested, applied, rejected) | none | later |

Already done: Job entity + migration, `IJobSource` + rate-limited fetch worker, Jobicy source, jobs API (`GET /api/jobs`, `/api/jobs/{id}`), rule-based scoring, configurable age window (default 7 days), cross-source de-duplication.
