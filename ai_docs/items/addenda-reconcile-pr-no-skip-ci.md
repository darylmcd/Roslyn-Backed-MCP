## Anchors

- `ai_docs/prompts/backlog-sweep-addenda.md` — no `skipCiToken` / CI-policy section today

## Acceptance

- The addenda states explicitly that reconcile / docs-only PRs in this repo MUST NOT carry a `[skip ci]` token, and why: branch protection lists `validate` as a REQUIRED status check, so suppressing the workflow leaves the PR permanently `BLOCKED` with no check that can ever satisfy the rule.
- The statement is placed where the execute flow will read it (near `## Build / validation commands` or a new `## CI policy` section).

## Evidence

Observed during sweep 20260819T180531Z. The global execute prompt's "Idea 9 — skip CI on the docs/state-only reconcile PR" advises a `[skip ci]` token in the head-commit subject. Applied to reconcile PR #1285 it suppressed every workflow; `gh pr view` then reported `mergeStateStatus: BLOCKED` with `no required checks reported on the branch`. Recovery was to amend the subject to drop the token and force-push so `validate` would run.

The global prompt cannot know this repo's branch-protection configuration — the caveat belongs in the repo-local addenda, which is the sanctioned place for repo-specific facts the global commands need.

Source: sweep 20260819T180531Z wave-A reconcile (PR #1285).
