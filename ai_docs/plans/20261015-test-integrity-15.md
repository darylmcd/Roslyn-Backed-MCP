# Test integrity remediation — 15 items

<!-- scope: reference -->
<!-- artifact: plan -->
<!-- status: reference -->

| Field | Record |
|---|---|
| Base | 96f3ea722ceb8f663ce3b2c46058309df92e2914; clean main equal to origin/main; zero open PRs. |
| Retained work | Extraction8a40da79 and restoreea969292 are clean source checkpoints with explicit execution holds. Preserve both and their open rows; no completion claim. |
| Workflow | Direct implementation with delegated disjoint file ownership; no backlog-remediate skill. Serialize local runtime gates. |
| Compatibility | Preserve production retry policy and released wire contracts; a required internal per-call delay seam supports deterministic native-I/O proof. |

| # | Row | Complete repair |
|---|---|---|
| 1 | server-probe-test-doubles-batch-1 | Share explicit mutable version-provider and list-only workspace fixtures across all seven server probe classes. |
| 2 | server-probe-test-doubles-batch-2 | Preserve exact defaults, timestamps and mutable state across metadata, heartbeat and catalog tests. |
| 3 | server-probe-test-doubles-batch-3 | Preserve update/wire statuses with explicit provider properties. |
| 4 | sampling-mrtr-workspace-fake-retire | Use the shared fail-closed list stub in the sampling harness. |
| 5 | unused-code-analyzer-test-harness-wave-1 | Extend existing fail-closed handlers and own analyzer fixture resources. |
| 6 | unused-code-analyzer-test-harness-wave-2 | Migrate dead-local, duplicate-helper and scan-failure suites without assertion changes. |
| 7 | unused-code-analyzer-test-harness-wave-3 | Migrate duplicate-method, metrics and cache suites while preserving callbacks/version changes. |
| 8 | validation-test-compile-stub-deduplication | Share cancellation-only compile behavior without changing timeout assertions. |
| 9 | format-range-test-fixture-lifecycle-deduplication | Unify sample-copy/write/load/close ownership with guaranteed disposal. |
| 10 | extraction-route-binding-test-fake-reuse | Share identical gate/store/service route fakes across every duplicate consumer; retain variants. |
| 11 | lru-eviction-test-hygiene-consolidated | Release and await the gated reader even after assertion failure. |
| 12 | top10-good-preview-result-contract | Require exact applied status and deterministic edited bytes in a clean isolated fixture. |
| 13 | test-os-guards-report-passed | Report unsupported OS as inconclusive; assert regex prerequisites. |
| 14 | file-operation-preview-state-assertion-contract | Observe tokens through a test-only contract decorator; assert redeemability without private reflection. |
| 15 | test-try-delete-directory-copies | Use shared bounded, link-safe cleanup at all seven copied helper sites; report cleanup failures. |

## Verification and delivery

1. Re-derive anchors and inventory every instance; preserve assertions and distinct behavioral variants.
2. Observe old-behavior failures for strengthened behavioral contracts; compare case counts for structural migrations.
3. Run every affected class, inspect TRX outcomes, and review implementation independently.
4. Run unsharded just ci on final source; retain all red runs and fix root causes.
5. backlog: sync ai_docs/backlog.md with canonical writer after focused proof; publish closures with implementation.
6. Commit, push, open PR, require exact-head hosted Windows/Linux/SDK-floor and CodeQL green, merge and clean up.
7. Verify clean main equals origin/main, zero open PRs, and preserve only explicitly held prior checkpoints.

## Current evidence

| Check | Evidence |
|---|---|
| Source review | Existing test cases and assertions retained except intentionally strengthened contracts; cross-review found and repaired missing imports, setup ownership, LRU task draining and disposal ordering. |
| Preview assertion red proof | Original reflection failed on a valid IPreviewStore wrapper; preview-red.trx records the concrete-field/target mismatch. |
| Apply status red proof | Wrong-status mutation failed the exact applied assertion with real edited file and zero pre/post errors; production source restored byte-exact. |
| Rejected artifact run | Copy restoration preserved source timestamp; PDB document checksum proved a normal incremental run reused the mutant binary. Reject that run and force a non-incremental rebuild before new proof. |
| Platform inventory | Thirteen early-return guards repaired; five Linux-only cases on Windows, not eight as the old row claimed. Both skill regex prerequisites now assert nonempty matches. |
| Existing independent defect | BoundedStore pre-insertion capacity defect remains tracked by bounded-store-capacity-atomic-insertion; no production store changes in this test-only batch. |
| Fresh build | Non-incremental Release build passed with zero warnings/errors; rebuilt PDB matches restored production source. |
| Closure staging | Canonical writer staged all15row closures with zero lint errors; closures remain conditional on final local and hosted gates and merge. Focused run passed682/0failed/16platform skips (698total). |
| Delivery gate | Require focused proof, unsharded just ci, exact-head hosted validation, confirmed merge and clean main before reporting completion. |

| Full-gate correction | Initial aggregate stopped at CRLF and two new import-order findings; normalized touched working files to LF and ran the formatter on both import lists. Fresh changed-file gate passed46files/0new/1tracked baseline import finding. No baseline edits or suppressed findings. |

## Forced gate prerequisite

| Field | Evidence |
|---|---|
| Row | atomic-file-writer-retry-test-wallclock-coupling, filed with canonical writer after the full gate failed. This is additional to the15selected rows. |
| Failure | Head310498e5 persistent-reader test exceeded its30s stopwatch assertion at32s. Known-red suite was stopped; private invocation temp cleanup completed. No passing full result claimed. |
| Complete fix | Keep production Task.Delay and native replacement/ownership checks; inject delay only through the existing internal writer seam. Assert exact9delays, native failure/cleanup, reader release and cancellation while awaiting backoff. |
| Red proof | Intentional11attempt mutation rejected the tenth scheduled delay in atomic-retry-red.trx; original source restored with fresh timestamp. |
| Final requirement | Fresh focused class, cold review, formatter, full unsharded local gate and exact-head hosted gates before merge. |

| Focused prerequisite proof | Complete atomic-writer class passed27/0failed/3platform skips (30total), including exact schedule, real Task.Delay failure/cleanup, reader release and pending-backoff cancellation. Conditional row closure accompanies implementation; final gates remain required. |
