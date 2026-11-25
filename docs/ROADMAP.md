# Sloffer roadmap (autopilot order)

Statuses: `todo`, `doing`, `done`, `blocked`, `later`. Update this file as you go. Specs are in `docs/next/`.

Goal of the product: find .NET-oriented jobs in EMEA that fit Sandro (see Product context in CLAUDE.md), rank them with rules (score + reasons), show them in a web UI.

| # | Item | Spec | Status |
|---|------|------|--------|
| 1 | Check what is already on main: is `score` and `scoreReasons` in `GET /api/jobs`? If not, do `jobs-api-score.md`. Mark `done` when the API has them. | `docs/next/jobs-api-score.md` | done (#13) |
| 2 | Arbeitnow job source | `docs/next/arbeitnow-source.md` | done (#10) |
| 3 | Jobicy extra .NET queries (needs item 2 merged first, both touch README/CLAUDE.md) | `docs/next/jobicy-queries.md` | done (#11) |
| 4 | Real-data check: apply migrations, run `Sloffer.Jobs`, look at the 20 best and 20 rejected jobs. Fix obvious scoring mistakes (wrong rejections, silly rankings) in small PRs with tests. Record findings in `docs/DECISIONS.md`. | none: use judgment | done (#14, #15) |
| 4b | Scoring v2, precision first: role gate (title decides), .NET centrality, seniority fit, score bands, API and UI default 55+. Golden tests first; 4 PRs (role gate + golden tests, centrality, seniority + bands + API default, frontend). | `docs/next/scoring-v2.md` | done (#16, #17, #18; frontend part moved to 4f) |
| 4c | Scoring v3, strict: English listings only, no required German/Dutch/French, other-language titles rejected, polyglot penalty, on-site without relocation capped at 70. Golden examples first. | `docs/next/strict-matching-v3.md` section 1 | done (#19) |
| 4d | Viewed: `ViewedAt` + mark/unmark endpoints + `hideViewed` filter (backend) | `docs/next/strict-matching-v3.md` section 2 | done (#20) |
| 4e | Remotive source, then Remote OK source (one PR each) | `docs/next/strict-matching-v3.md` section 3 | todo |
| 4f | Frontend: Excellent-only default, 100 per day, band colours, source badges, Viewed (sloffer-frontend) | `docs/next/strict-matching-v3.md` sections 2 and 4 | todo |
| 5 | Frontend MVP in `sloffer-frontend` | `docs/next/frontend-mvp.md` | done (sloffer-frontend#1) |
| 6 | AI scoring of the top N jobs per day | none | later (do not start without asking Sandro) |
| 7 | Himalayas source (needs a free API key from Sandro) | none | blocked: needs key |
| 7b | Adzuna source: large EMEA coverage, the realistic way to ~100 matches a day (needs a free API key from Sandro) | `docs/next/strict-matching-v3.md` section 3 | blocked: needs key |
| 8 | Per-job status in the UI (new, interested, applied, rejected) | none | later |

Already done: Job entity + migration, `IJobSource` + rate-limited fetch worker, Jobicy source, jobs API (`GET /api/jobs`, `/api/jobs/{id}`), rule-based scoring, configurable age window (default 7 days), cross-source de-duplication.
