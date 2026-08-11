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
- `interface-extraction-conflict-check-hardening`: landed (baseRef=9b2411339bf2d8bf0a06ebaa0b495f8f78c27dcc, PR #1213, merge 49384715; 2 medium code-quality findings filed as spin-off row interface-extraction-catch-chain-dead-rethrow; cleaned up a stale uncommitted worktree at .worktrees/topn-row2 left by the crashed prior session, same row, superseded weaker fix)
- `overallstatus-verdict-table-remaining-restatements`: landed (baseRef=493847153b6bd22180e2ad255c42bec47ae22051, PR #1214, merge 63000b42; S-row light ceremony, single combined review verdict pass, 0 findings)
- `compile-check-buildhint-whitespace-discriminator-mismatch`: landed (baseRef=63000b428a8f6edfab4d1c522b68585ccd51df5f, PR #1215, merge e35fede7; 1 spec-compliance fix cycle — missing changelog fragment; 1 low code-quality finding filed as spin-off row isolated-workspace-slnx-surgery-consolidation)
- `fixall-blank-projectname-silent-wrong-target`: landed (baseRef=e35fede79bf3efe3eb5fdfe5663c1fe31498be7c, PR #1216, merge 2b98e58c; both reviews pass; 2 spin-off rows filed — fixalltools-projectname-stale-optional-description, fixall-scope-required-validation-hoist)
- `compilation-cache-analyzers-entry-guard`: landed (baseRef=2b98e58ce268d9c52ebff61b5f8a8e5f2def207b, PR #1217, merge 656efda2; both reviews pass; 1 medium spin-off row filed — compilation-cache-cancellation-test-contract-drift)
- `elicitation-trychoice-cancellation-swallow`: landed (baseRef=656efda25126fc82bad72b3974e8da55fdca2377, PR #1218, merge cadc7e42; spec-compliance re-review adjusted bullet 2 scope after a genuine in-flight-cancellation test proved unbuildable — deadlocked across 3 fix attempts, root cause deferred; 3 spin-off rows filed — elicitation-inflight-cancellation-test-harness-deadlock, elicitation-inmemory-harness-consolidation, elicitation-tryelicitchoice-swallow-path-coverage; 1 existing row noted — elicitation-doc-drift-and-delegate-chain)
- `test-run-unfiltered-bare-error-rootcause`: implemented (baseRef=cadc7e42ce3e72724f388aad634c6610575de7f2; 2 spec-compliance fix cycles — root cause was mis-attributed, then the confirmed fix was incomplete; final root cause: WorkspaceExecutionGate's internal-timeout OCE escaping unclassified past the MCP SDK's bare-error catch-all; 4 spin-off rows filed — gate-owned-timeout-cts-oce-classification-audit, test-run-failures-pagination-truncation, gate-timeout-exception-drops-inner-oce)

## Final step

- `backlog: sync ai_docs/backlog.md` after each row lands (remove the row + its `items/<id>.md`, per `/close-backlog-rows`).
