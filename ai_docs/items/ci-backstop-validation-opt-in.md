# ci-backstop-validation-opt-in — Authorize the existing validation-only post-merge workflow

**row:** `ci-backstop-validation-opt-in` · **pri:** `Medium` · **size:** `S` · **deps:** `—`

## Anchors

- `.github/workflows/ci.yml`
- `CI_POLICY.md`

## Acceptance

- [ ] Add the canonical explicit validation-only backstop opt-in to ci.yml after confirming it does not publish or deploy external artifacts.
- [ ] Preserve PR/merge-group full topology, manual/schedule informational context, permissions, and existing trigger policy.
- [ ] Document which manual validation the backstop authorizes and that it cannot satisfy required validate or authorize package/release publication.
- [ ] Observe the canonical decision fail for existing unmarked workflow and select only ci.yml for the marked candidate; do not run global producer gates.
- [ ] Cold-review exact candidate head, pass hosted required checks, merge, then dispatch the canonical backstop once after the current wave and inspect its actual result.

## Evidence

| Evidence | Observation |
|---|---|
| PR1753 merged 5e0871fe20f59ce90292eb05a645b0ea1fdd9ada | land actual exit0, required six-platform-leg aggregate passed; post-merge backstop decision=fail because no push trigger or opted-in validation workflow exists. |
| .github/workflows/ci.yml:1-20 | Existing workflow_dispatch/schedule validation, contents/pull-requests read only, intentional absence of push-to-main, no canonical opt-in marker. |
| CI_POLICY.md:162-167 | Manual/schedule report validate-informational; PR validate depends on full matrix and SDK floor. |
| Canonical installed bsweep-state.mjs:3440-3476 | Only exact comment `# ci-backstop: validation-only` authorizes a manual validation workflow; manual publisher/deployer availability is insufficient. |

## Context

- Repository prerequisite for current remediation closeout; original count=15 selection remains unchanged.
- Change repository configuration only; global tooling remains under its coordinator ownership.

## Checkpoint — 2026-10-06

| Proof | State |
|---|---|
| Draft PR #1756; head bab318400349c76c2b62039d280746aff03f4b5c; base 8c9f851194b9d6586aeaa1900bbba84043f992d5 | Exact three paths committed through staged pre/post guards and immutable normal push; draft remains open. |
| Pinned actionlint 1.7.12; AI-doc/skills/notice checks; fragment contract | Native exit0; nonempty logs and hash/time/private-environment receipts in session scratch. |
| Canonical pure backstop decision | Old workflow fails; candidate selects only ci.yml. No manual workflow dispatched. |
| Fresh cold review | Agent creation refused at root and fresh child with thread limit. No review or merge authorization; continue with fresh cold-review capacity and required hosted checks. |

PR: https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1756. Original count=15 selection unchanged; this remains a tracked repository prerequisite.
