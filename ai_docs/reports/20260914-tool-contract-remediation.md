# Tool contract remediation

<!-- scope: in-repo -->

## Selection

| Constraint | Decision |
|---|---|
| User ceiling | Up to 15 existing rows; selected 3 |
| Baseline | `main` at `c2eff84c`; clean checkout, one worktree |
| Priority | No Critical or High rows; selected bounded Medium tool-contract work |
| Execution | Direct inspection and canonical backlog writer; no backlog-sweep skill |
| Final action | backlog: sync ai_docs/backlog.md |

| Selected row | Delivered | Evidence |
|---|---|---|
| `preview-refusal-public-reasons` | Six type-move refusal sites opt into public, server-authored recovery messages; remove path and caller-name interpolation | TypeMoveTests covers safe messages and gated tool/classifier propagation; successful preview/apply and root rejection remain covered |
| `method-diet-ratchet-only-clean-slices` | Shared harness ratchet for 15 tools; 200-character individual ceiling, measured 1,499-character aggregate with a 1,520-character ceiling; preserve discovery triggers and destructive warning | MethodDescriptionDietRatchetOnlyCleanSlicesTests |
| `method-diet-restructure-interface` | Reconciled stale open row after inspecting existing production descriptions and shared ratchet | MethodDescriptionDietRestructureInterfaceTests passes against current source; no additional production rewrite needed |

## Other candidates

| Candidate family | Disposition |
|---|---|
| Promotion scorecard and dependent promotions | Separate complete surface-audit workflow; do not replace the scorecard with partial evidence |
| Tool consolidation | Dependency cells still name a retired prerequisite; existing `tool-consolidation-merge-child-dependency-repair` owns reconciliation before claiming merges ready |
| Unknown workspace category | Detail actually spans at least five production files including resource-error mapping; requires splitting plus published-contract decision and migration treatment |
| Test-run timeout envelope | Current gate reclassifies its deadline; keep row open because the existing gate-only tests do not establish the requested full tool-boundary controlled-cancellation regression |
| Coverlet upgrade / coverage refresh | Separate dependency and Windows coverage/event-log verification; upstream availability not checked in this batch |
| Test assembly serialization audit | Declared L; split by the live shared-state cause before implementation |
| Formatter contention and provider/resource failures | Reproduction-led investigations; no speculative production change |
| Git process lifetime / scripting causal barrier | Separate lifecycle concern with controlled-process regressions |
| Server update status, server guidance, retro extraction, doc-link gate | Independent contracts; retained rather than padding this tool-refusal/description batch |
| Reserved and Defer rows | Excluded |

## Adjacent findings

| Row filed | Finding |
|---|---|
| `type-move-nested-and-ambiguous-selection` | First simple-name descendant match can select a nested or ambiguous declaration and change ownership/visibility |
| `type-move-namespace-import-preservation` | New unit loses enclosing namespaces and namespace-local imports; generic-name regex is not semantic import resolution |
| `type-move-unused-using-failure-observability` | Catch-all using cleanup swallows cancellation and unexpected failures |
| `tool-error-classifier-exception-specificity` | Dictionary insertion order controls derived/base exception classification |

These follow-ups retain separate regression shapes and bounded production anchors. They are not claimed fixed by exposing refusal reasons.

## Validation

- Roslyn workspace reload: ready, zero workspace diagnostics.
- Roslyn compile_check: success, zero compiler errors across six projects.
- Focused test_run: 21 passed, zero failed or skipped, including both description slices, TypeMoveTests, and MoveTypeDiskStateTests.
- Full local and hosted gates are required before landing; final ship evidence belongs in the PR and task closeout.
