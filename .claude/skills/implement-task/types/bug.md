# Bug playbook

Something behaves wrongly. Fix the cause, not the symptom.

1. Reproduce first: write a failing test that shows the bug. If you cannot reproduce it from the task and the code, fail with a comment saying what you tried. Do not guess a fix.
2. Find the root cause and say it in one sentence (it goes in the PR).
3. Make the smallest change that fixes it. No refactors, renames or unrelated cleanups.
4. The new test must pass now and must have failed before the fix. Keep it as the regression test.
5. If the fix needs a model change, add a migration. If it needs an API contract change, flag it in the PR (the frontend depends on it).
6. PR body: symptom, root cause, fix, regression test. If you found other bugs, list them as follow-ups; do not fix them here.
