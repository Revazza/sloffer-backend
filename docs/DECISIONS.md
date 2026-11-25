# Decisions made during autopilot

Append one short entry per decision: date, what was decided, why. Newest last.

- 2026-11-25: Autopilot started. Claude may merge its own PRs when build and tests pass. Matching is rules-first (see `claude/MATCHING_DESIGN.md` in the Sloffer Project for background); AI scoring is deferred.
- 2026-11-25: Jobs API `minScore` hides only scored jobs below it; unscored jobs (score null) are always listed and sort last under `sort=score`. Why: the spec asks for "nulls last" ordering (so nulls must be visible by default) and unscored rows have not been judged yet, so hiding them would silently drop jobs until a rescore runs. `sort` is case-sensitive (`score`/`posted`), matching the other lowercase query values. (PR #13)
