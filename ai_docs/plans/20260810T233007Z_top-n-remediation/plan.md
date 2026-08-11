# Top-N Remediation Plan — 20260810T233007Z (count=8)

## Selection

| id | rank | reasons | estimated file touches |
|----|------|---------|-------------------------|
| `ci-runner-offline-hosted-fallback-router` | 1 | Medium pri, shovel-ready, 0 strong/medium signals | S (~1 prod: workflow yaml) |
| `interface-extraction-conflict-check-hardening` | 2 | Medium pri, shovel-ready, 0 strong/medium signals | S (1 prod + 1 test) |
| `overallstatus-verdict-table-remaining-restatements` | 3 | Medium pri, shovel-ready, 0 strong/medium signals | S (docs, 2 files) |
| `compile-check-buildhint-whitespace-discriminator-mismatch` | 4 | Medium pri, shovel-ready, 0 strong/medium signals | S (1 prod + test) |
| `fixall-blank-projectname-silent-wrong-target` | 5 | Medium pri, shovel-ready, 0 strong/medium signals | M (1-2 prod + tests) |
| `compilation-cache-analyzers-entry-guard` | 6 | Medium pri, shovel-ready, 0 strong/medium signals | M (1 prod + tests) |
| `elicitation-trychoice-cancellation-swallow` | 7 | Medium pri, shovel-ready, 0 strong/medium signals | M (1 prod + tests) |
| `test-run-unfiltered-bare-error-rootcause` | 8 | Low pri, shovel-ready; substituted in after `audit-21-analyzer-load-decision` (originally rank 1) was excluded — see Selector Discards below | M (investigate root cause, then fix) |

**Selector discards (orchestrator judgment, applied after subagent selection):**
- `audit-21-analyzer-load-decision` (originally selector rank 1): the selector itself flagged this as non-implementable — its own Acceptance criteria requires a human product decision ("Decision recorded: (a) ... OR (b) ..."), not agent-executable work. Excluded by the orchestrator per the selector's own Readiness Notes caveat. Left open on the backlog for operator disposition.
- `tool-surface-pagination-or-tool-sets` (Low tier, next in file order after the row above): explicit weak-evidence flag in its own `do` cell ("Weaker evidence — N until small-model discovery friction is reported after the router lands externally") — intentionally deferred pending external evidence, not implementation-ready this session.
- Replacement candidate `test-run-unfiltered-bare-error-rootcause` selected by the orchestrator (next Low-tier shovel-ready row in backlog file order) to fill N=8.

**Sweep-shaped / dep-blocked rows skipped (selector output, unchanged):** `promotion-tier-execution-batch`, `core-dto-location-quartet-consolidation-secondary`, `core-dto-location-quartet-stage-followups`, `surface-test-audit-artifact-gate-and-scorecard-staleness`, `compilation-cache-wire-group-c-consumer`, `shipped-skills-hardcode-bare-roslyn-tool-prefix`, `elicitation-doc-drift-and-delegate-chain`, `group-c-compilation-cache-gate-hardening`, `host-tools-cohesion-split`, `project-mutation-service-stale-comment-and-naming`, `refactoring-code-fix-preview-decomposition`, `symbollocatorfactory-drift-tool-test-gap`, `workspace-id-optional-readonly-surface-full-sweep`, `analysis-services-hardcoded-parallelism-clamp-magic-numbers` — recommend `/backlog-sweep:prepare` for the sweep-shaped set (several have concrete Proposed Splits from this session's backlog-row-prep pass; see report captured below).

## Plan-collision check

All 12 `ai_docs/plans/*_backlog-sweep/state.json` plans are `phase: complete`, every initiative terminal (merged/deferred). No non-terminal initiative claims any selected row. Clean.

## Orphaned-PR sweep (step 2)

`gh pr list --state open` → 4 open PRs, all dependabot (#1198, #1197, #1082, #1079). No backlog row ids on any open PR. Exclusion set: empty.

## Row state lines

- `ci-runner-offline-hosted-fallback-router`: landed (baseRef=fb935d58491ecc02595ebdc748950c1a0f56ddd3, PR #1211, merge 07384b59a0c2c8368e22b3c42a2faac6db3310f9; 1 code-quality fix cycle — HIGH: catch-block missing exit 0; 2 backlog sketches filed as follow-on rows)
- `interface-extraction-conflict-check-hardening`: implementing (baseRef=9b2411339bf2d8bf0a06ebaa0b495f8f78c27dcc)
- `overallstatus-verdict-table-remaining-restatements`: selected
- `compile-check-buildhint-whitespace-discriminator-mismatch`: selected
- `fixall-blank-projectname-silent-wrong-target`: selected
- `compilation-cache-analyzers-entry-guard`: selected
- `elicitation-trychoice-cancellation-swallow`: selected
- `test-run-unfiltered-bare-error-rootcause`: selected

## Final step

- `backlog: sync ai_docs/backlog.md` after each row lands (remove the row + its `items/<id>.md`, per `/close-backlog-rows`).
