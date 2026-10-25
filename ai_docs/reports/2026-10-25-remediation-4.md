# Four-row remediation — 2026-10-25

<!-- scope: in-repo -->

| Field | Evidence / claim boundary |
|---|---|
| Request | `/remediate count=10`; ceiling 10, selected 4 independently complete rows. |
| Source baseline | `e17fae0da50186b00799e9d8b21ad4861f2cff3b`; clean primary fast-forwarded before implementation. |
| Source delivery | All three implementation PRs below are merged after independent approval and required hosted Windows/Linux/SDK-floor checks. |
| Canonical closure | This documentation change closes only the four delivered rows through `backlog.mjs`. |
| Final batch gate | Root must run full `just machine-validate ci` on clean final merged main after this documentation change. The landing PR body owns the later exact-head receipt; implementation acceptance and scoped checks do not establish batch completion. |

## Rows and delivery

| Row | PR / reviewer verdict | Reviewed head | Verified merge |
|---|---|---|---|
| `msbuild-metadata-evaluation-collection-disposal` | [1829](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1829); APPROVE | `920c650fe90f014598adc8685e674f4b5800c129` | `49a4f11d1eb707c4539e254c4574a20501500429` |
| `workspace-explicit-reload-double-transition` | [1831](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1831); APPROVE after cleanup correction | `586a7d06f791ee1dcf854e587776fd59df648bc8` | `a507e7e09afe975e36eda52c2467a9da29c79f7c` |
| `testbase-workspace-cap-comment-drift` | [1830](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1830); APPROVE after contradictory-comment corrections | `19035652459f7e0be2feb3c5676d4f38ccbfd94d` | `c208e8c0f33faf0765952bc721d64d2926e6b301` |
| `rebase-persistence-routing-comment` | [1830](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1830); same reviewed delivery | `19035652459f7e0be2feb3c5676d4f38ccbfd94d` | `c208e8c0f33faf0765952bc721d64d2926e6b301` |

| Mechanism | Complete correction / verification |
|---|---|
| Explicit reload | Only the explicit caller skips automatic staleness processing. Native stale/nonstale reload advances once; default/custom preview ranges, strict fork ordering, cancellation and intentional post-restore refresh remain covered. Semantic sweep found one equivalent wrapper among 48 reload references. |
| Metadata collection ownership | Dispose throwaway evaluation collections; dispose failed cache ownership transfer; guarantee Dispose is invoked after cached unload failure. All five production allocations were classified; the other three already use scoped disposal. Resolver/JIT ordering, globals, property order and exception classification remain intact. |
| Fixture and persistence comments | Correct capacity/lifetime ownership and all discovered obsolete/contradictory apply, snapshot and persistence pointers. No executable code or assertions changed. |

## Evidence

| Qualification | Observed result |
|---|---|
| Reload old behavior | Two native stale cases failed: expected version 2, actual 3; both nonstale controls passed. Strengthened ordering assertion also failed with the ambiguous old observer. |
| Reload committed head | 106 passed, 0 failed, 0 skipped; 2m11s on `586a7d06`. Four affected native cases passed again after the fixture cleanup correction; the later committed run includes that correction. |
| Ownership old/current | Scratch compiled-IL/control-flow proof changed from three missing guaranteed cleanup paths to zero. Native event subscribers remained after unload and detached on disposal; no OS-handle or memory-leak claim. |
| Ownership committed head | 48 passed, 0 failed, 0 skipped; 1m13s on `920c650f`; includes actual helper success, invalid XML/missing-import classification and failed-cache recovery controls. |
| Comment committed head | All 39 changed C# lines are comments; formatter, documentation, fragment and whitespace checks passed on clean unchanged `19035652`. |
| Builds and compiler | Scoped build/compile checks passed; no new formatter findings. One existing IMPORTS allowance is tracked by the bounded follow-up below. |
| Hosted source gates | Four Windows legs, two Linux legs, exact SDK floor 10.0.400, CodeQL and required `validate` passed for each implementation head. |
| Reviewed-to-merged files | `git diff --quiet` returned 0 for every implementation's complete changed-file set against its verified merge. |
| Fresh closeout audit | APPROVE implementation acceptance and calibrated follow-up edits; actual closure required the now-verified source merges. |
| Final full gate | Authoritative later result belongs in this report's landing PR body, including SHA, native exit, totals, skipped-case accounting and clean-current-main verification. No result is preclaimed here. |

All local build/test checks used the shared machine validation helper. MCP semantic reads and compile checks were used; CLI test runs supplied the mandatory process-admission wrapper absent from the `test_run` request schema.

## Existing-work and follow-up disposition

| Item | Verified treatment |
|---|---|
| Filing [1821](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1821) | Merged as `6dae187faf0041425ee583718eeb50a899fa636f`; corrected unsupported reproduction specificity and a dangling already-delivered dependency without changing graph acceptance. |
| `workspace-cache-fixture-cleanup-failure-silent` | Extended to three concrete silent-cleanup fixtures; shared helper remains reuse context. No cleanup-fault runtime reproduction claimed. |
| `preview-token-consumed-reason` | Added `RefactoringService.InvalidPreviewResult` to the shared classification census; source-only observation, not a newly established runtime cause. |
| `path-identity-ordinal-ignorecase-services` | Removed the empty `skipPaths` set from behavioral defect anchors: its comparer currently changes no behavior. Other families remain open. |
| `msbuild-evaluation-service-import-order` | New bounded follow-up: one service plus its baseline entry; preserves bindings and unrelated allowances. |
| `undo-reload-failure-reports-success` | Existing row retains the unrelated reload-failure reporting defect; no inline scope expansion. |
| Draft [1806](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1806) | Preserve the wider unapproved architecture proposal and its product-decision hold. No implementation authorization inferred. |
| Earlier worktrees and stash | Preserve retained extraction/restore source and original stash. Shipping helpers removed this batch's clean source/filer worktrees; this documentation worktree is removed at landing. |

## Selection accounting

| Frozen baseline selection disposition | Rows |
|---|---:|
| Selected | 4 |
| Explicit Defer band | 11 |
| Recorded execution/contributor hold | 42 |
| Multi-shape work needing decomposition | 19 |
| Separate mechanisms outside the selected complete fixes | 264 |

The 340-row selection snapshot predates filing 1821; these are exclusive selection dispositions, not canonical eligibility totals. Per-row reasons and raw logs are retained under `<user-profile>/.codex/scratch/roslyn-remediate-20261025/` (`selection.csv`, component receipts, TRX, review and merge evidence).

Canonical documentation candidate count after closures and the new bounded follow-up: 338 open, 327 actionable, 262 eligible. Defer and genuinely unmet work remain open; nothing is counted as delivered solely from a draft or scoped checkpoint.
