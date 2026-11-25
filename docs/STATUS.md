# Autopilot status (2026-11-25)

## Done today
- Jobs API: `score`, `scoreReasons`, `minScore`, `sort` (#13); `viewedAt`, mark/unmark viewed, `hideViewed` (#20).
- Scoring fixes from real data (#14, #15), scoring v2 (#16-#18: role gate, .NET centrality, seniority, bands) and v3 (#19: English listings only, no required German/Dutch/French, other-language titles rejected, polyglot penalty, on-site without relocation capped at 70). 30 golden examples in `tests/Sloffer.Tests/Matching/GoldenJobs/golden-jobs.json`.
- Sources: Remotive (#21), Remote OK (#22), on top of Jobicy and Arbeitnow.
- Frontend (sloffer-frontend #1-#3): ranked list and detail, Excellent-only default (last 24 h, up to 100), band colours, source badges, Viewed (set when the original posting link is clicked), hide viewed.
- Dev DB: all migrations applied (incl. `AddJobViewedAt`), all jobs rescored to scorer version 7.

## Current numbers (last real run)
155 jobs; 3 Excellent (all remote .NET), 2 Good, 144 rejected. Excellent posted in the last 24 hours: 0. See `docs/DECISIONS.md` for details.

## Blocked / waiting on Sandro
- **Volume:** the free remote boards add very few .NET EMEA jobs; Remotive's free feed is delayed by days. Adzuna (roadmap 7b) needs a free API key (developer.adzuna.com: app id + key). Himalayas (7) also needs a key.
- **Default window:** asked whether the list should default to the last 7 days instead of 24 hours while volume is low. Not changed without an answer.

## Next
1. Adzuna source as soon as there is a key (largest gain).
2. Then roadmap 6 (AI scoring of the top jobs, only after asking) and 8 (per-job status beyond Viewed: interested / applied / rejected).

## Notes for the next run
- Before building the backend on Windows, stop a running `Sloffer.Api` (it locks its build output).
- Jobicy allows one fetch per hour; any `Sloffer.Jobs` start fetches immediately, so space worker runs at least an hour apart.
- This run merged 18 PRs (over the 5-PR limit in AUTOPILOT.md) because Sandro asked for each step directly in the conversation.
