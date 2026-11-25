# Scoring v2: precision first

Problem (real data, 2026-11-25): the top of the list contains a "Senior Manager" job and a "Senior QA Engineer" job. They matched because `.NET`/`C#` appeared somewhere in the description. Matching is not enough. We want the **best** matches, and the list must be short and trustworthy.

Principle: **the job title decides what the job IS. The description only decides how well it fits.** A keyword in the description never turns a non-engineering job into a match.

## 1. Role gate (hard rule, score 0 with a reason)

Reject (score 0, reason "role: <what matched>") when the **title** says the job is not a software engineering IC role. Use word-boundary, case-insensitive matching on the title only.

Reject title terms (non-exhaustive, put them in config, not code):
- QA / testing: `qa`, `quality assurance`, `test engineer`, `tester`, `sdet`, `test automation`, `quality engineer`
- management: `manager`, `director`, `head of`, `vp`, `vice president`, `chief`, `cto`, `team lead` is NOT rejected here (see section 3)
- non-engineering: `sales`, `account executive`, `recruiter`, `talent`, `marketing`, `support`, `customer success`, `designer`, `ux`, `analyst` (data/business/risk), `consultant` (unless the title also has developer/engineer), `project manager`, `product owner`, `scrum`, `administrator`, `technician`
- data/ML specialisations that are not backend: `data scientist`, `data engineer`, `machine learning`, `ml engineer`
- seniority too low: `intern`, `trainee`, `junior`, `graduate`, `entry level`

Accept only if the title ALSO contains an engineering word: `developer`, `engineer`, `software`, `backend`, `back-end`, `full stack`, `fullstack`, `architect`, `programmer`, or `.net`/`c#` as a standalone term. A title with none of these is rejected with reason "role: title is not an engineering role".

`Senior Manager ...` and `Senior QA Engineer ...` must both come out as 0.

## 2. .NET centrality (this replaces "does the text mention .NET")

How central is .NET to the job? Compute a level, store the reason:

- **core** (strong): `.NET`, `C#`, `ASP.NET` or `dotnet` in the title, OR in the first part of the description that lists requirements/must-haves/tech stack.
- **mentioned** (weak): appears only once, or only inside an "or" list with other languages ("Java, Go or .NET"), or only under "nice to have" / "bonus".
- **absent**: not a .NET job, already a hard reject today.

Score contribution: core is worth a lot, mentioned is worth little and cannot reach the "good" band alone. A job with .NET only as "mentioned" must stay below the default list threshold.

Implementation hint: split the description at headings like "nice to have", "bonus", "preferred", "plus" and treat what comes after as weak. Count .NET-family terms outside that part.

## 3. Seniority fit (Sandro: about 4 years, backend, mid to senior)

- `senior`, `sr`, `mid`, `middle`, plain "Software Engineer/Developer": good fit.
- `lead`, `team lead`, `tech lead`: small penalty, not a reject.
- `staff`, `principal`: bigger penalty (needs 8+ years usually).
- Years of experience asked: 3-6 good; 7-9 penalty; 10+ strong penalty; already in code, keep it.

## 4. Score bands (so the UI can be strict)

- 75-100 "Excellent": role gate passed, .NET core, backend, EMEA, years fit.
- 55-74 "Good".
- 35-54 "Maybe".
- 1-34 "Weak". 0 = rejected.

The API default should hide anything under 55. The frontend default for minScore should be "Good (55+)", with "Any" still available. Update the score badge colors to match the bands.

## 5. Tests: golden examples (do this first)

Create `tests/.../Scoring/GoldenJobs/` (or a JSON fixture) with real-looking jobs and the expected band. Minimum set:

- "Senior Manager, Engineering Operations" with .NET in the text -> 0.
- "Senior QA Engineer (C#, Selenium)" -> 0.
- "Senior .NET Backend Developer", remote EMEA, requirements list C#/ASP.NET Core/PostgreSQL -> Excellent.
- "Software Engineer" where .NET appears only in "nice to have" -> below 55.
- "Java Developer (or .NET)" -> below 55.
- "Junior C# Developer" -> 0.
- "Staff Engineer, C#" -> penalised, not Excellent.
- "Data Engineer (C# a plus)" -> 0.

Then pull the real data: list the top 40 and the 40 lowest non-zero scores from the dev database, and look for more mistakes. Every mistake found becomes a golden example before the fix. Record findings in `docs/DECISIONS.md`.

## 6. Rules

- Bump `JobScorer.Version` (rescore on startup already exists).
- Terms and weights in config (`JobScoring` options), not hard-coded.
- Each score keeps reasons: the UI shows why a job passed or failed. Rejections for role must say which title term matched.
- Build with 0 warnings, tests green. Small PRs: 1) role gate + golden tests, 2) .NET centrality, 3) seniority + bands + API default, 4) frontend default threshold and badge colors (sloffer-frontend).
