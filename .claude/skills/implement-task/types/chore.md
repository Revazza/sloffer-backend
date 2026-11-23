# Chore playbook

Maintenance with no new behavior: refactor, dependency bump, config, docs, tooling, test cleanup.

1. Behavior must not change. The existing tests must pass unchanged; edit a test only when it references something you renamed or moved.
2. No new features and no bug fixes mixed in. Note anything you spot as a follow-up in the PR.
3. Dependencies: stay on 10.x (11.x will not install). Bump only what the task names. Run build and the full tests afterwards.
4. Keep the diff small and reviewable. If the task is too big for one PR, do the first coherent part and list the rest as follow-ups in the PR.
5. Docs and config changes: keep `CLAUDE.md`, README and appsettings consistent with the code. No secrets.
6. PR body: what was changed, why it is safe (what proves behavior is unchanged), and how to verify.
