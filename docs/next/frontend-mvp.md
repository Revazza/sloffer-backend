# Frontend MVP: ranked jobs list (repo: sloffer-frontend)

## Goal
A small web app that shows the jobs from the Sloffer API, best matches first, with the reasons for each score. Single user, runs locally.

## Setup (the repo currently has only a README)
- Vite + React + TypeScript (strict), npm, ESLint, Vitest + React Testing Library.
- Add a `CLAUDE.md` (layout, commands, conventions) and copy `.claude/skills/implement-task/SKILL.md` and `.claude/settings.json` from `sloffer-backend`, adapted to npm (`npm run lint`, `npm test`, `npm run build`).
- API at `http://localhost:5010`; contract at `/openapi/v1.json`. Use a Vite dev proxy for `/api` so no CORS setup is needed. Generate typed API code from the OpenAPI document with `openapi-typescript` (a script `npm run api:generate`); commit the generated file.

## Acceptance criteria
- [ ] Jobs page: filters for text (`q`), location, source, minimum score (default 1), sort (score or posted), posted within (24h / 3 days / 7 days), include duplicates toggle. Filters live in the URL query string (shareable, survives refresh).
- [ ] List with paging: title, company, location, score badge, posted time ago, the first two score reasons. Empty, loading and error states.
- [ ] Job detail (route `/jobs/:id`): full description (HTML sanitized with DOMPurify), all score reasons, a clear "Open original posting" link, source and posted date.
- [ ] Footer credits the sources ("Jobs from Jobicy, Arbeitnow") with links to their sites.
- [ ] Clean, simple, readable design; works on a phone screen. Decide small design details yourself and note them in `docs/DECISIONS.md`.
- [ ] Tests for the filter-to-query-string logic, the list rendering states and the detail page; `npm run lint`, `npm test` and `npm run build` all pass with 0 warnings.
- [ ] README explains how to run backend and frontend together.

## Out of scope
Login, saved or hidden jobs, per-job status, deployment, dark mode, backend changes (if the API lacks something small and necessary, make it a separate backend PR first and say so in `docs/DECISIONS.md`).

## Done when
Lint, tests and build pass, the app runs against the local API and shows real jobs, PR merged.
