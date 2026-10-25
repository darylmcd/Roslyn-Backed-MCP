# Restore delivery closeout

| Field | State |
|---|---|
| Report status | Delivered-scope qualification and explicit scope transfer for PR1825. Canonical closures take effect on main only after this documentation PR merges. Final batch completion requires the post-documentation full gate. |
| Operator scope | Finish the delivered restore chain, transfer genuinely unmet work into independent nonblocking rows, decide PRs 1826/1825/1806/1809, then end this run. No further native implementation is authorized. |
| Verified source | Main `7f877e84d76eaead701a5e1bd33d297f21ac1497`; PR [1828](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1828) merged at `2026-10-25T04:39:54Z`. |
| Reviewed candidate identity | Candidate `0707b3e624e6ac836a6686b61cfd232c7d4403be` and merge `7f877e84` have identical whole Git tree `de5ca9ced983a75020d5529e8c718ab33ff605e7`; immutable tree comparison returned no difference. |
| Hosted evidence | All four Windows legs, both Linux legs, exact SDK floor `10.0.400`, CodeQL and required aggregate `validate` succeeded on candidate `0707b3e6`. |
| Documentation identity | PR [1825](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1825) preserves checkpoint `74923ee898d6e58bede402b309f293a1b94a09c4` and adds the graph transfer, qualified closures and this report. Its exact reviewed head, documentation checks, hosted checks, merge and final full-gate receipt are recorded in the PR description. |

## Original acceptance and explicit transfer

The original full acceptance is **not all met**. Parent closure records delivered source behavior and an operator-authorized scope transfer; it does not certify the open fork graph contract.

`MET` below describes inspected merged implementation, committed regressions and delivered-scope runtime qualification. The independent graph contract retains its own future qualification.

| # | Original acceptance, verbatim | Merged-main status and source evidence | Exact transfer to revised `restore-fork-reviewed-project-graph` acceptance |
|---|---|---|---|
| 1 | One shared required restore plan preserves each relevant project's recorded nullable package root with platform identity; uniform roots restore the target once, mixed roots restore every dependency project with --no-dependencies and one total timeout/cancellation budget. | **PARTIAL.** Source callers share `RestoreInvocationPlanner.cs:134`, root capture at `:154` and invocation rendering at `:256`; `WorkspaceTools.cs:644` and `TestRunnerService.cs:383` consume the plan. Fork restore still emits raw arguments at `WorkspaceForkApplyService.cs:796`. | **Graph A1:** source nullable roots before exclusions; shared complete plan; platform identity; uniform target once; mixed dependency order with `--no-dependencies`; one continuing budget and cancellation. |
| 2 | Test-runner restore precedes resolved-version authorization and retains --no-restore test execution. Capture source assets before copying fork exclusions; verify actual copied/evaluated project graph and source isolation before restoring it. | **PARTIAL.** `TestRunnerService.cs:347` restores before resolved-version authorization and retains no-restore execution. `ForkGraphPreparation.cs:46` checks evaluated copied source isolation. `WorkspaceForkApplyService.cs:250` copies before source assets capture; reviewed intended-graph equality is absent. | **Graph A1 and A3:** capture source assets before exclusions; compare replayed, evaluated fork contexts and graph to reviewed intent before restore. Test-runner sequencing and source isolation are delivered. |
| 3 | Ordinary accepted reference-changing previews retain intended graph changes and current sibling edits. Reuse shared physical mutation rendering for source apply, preview diffs/path visibility and isolated fork replay; preserve encoding, reference metadata, rollback and token authority. Concrete unrepresentable snapshots fail before copy or runner effects; do not blanket-refuse representable graph changes. | **PARTIAL.** Shared rendering is used by `DocumentSetPersistenceService.cs:104`, `SolutionDiffHelper.cs:117` and `PreviewStore.cs:217`; source rebase is at `RefactoringService.cs:838`. Physical capture now covers changed documents and project XML at `:415` and `:1118`. Fork still rejects earlier source versions at `WorkspaceForkApplyService.cs:324` and replays documents only at `:848`. | **Graph A2 and A3:** current-state rebase; sibling/reference metadata preservation; shared physical preparation before copy; concrete representability refusal; project/document replay and fidelity; encoding, rollback and source bytes/version/snapshot/token/Undo authority. Separate native-default policy and source-encoding rows remain open. |
| 4 | Guard valid malformed JSON kinds at every assets reader access; retain alternate layouts, missing/default roots, Linux case identity, redaction and cancellation. | **MET for delivered scope.** `ProjectMetadataParser.cs:37` guards root/libraries kinds; `RestoreStalenessDetector.cs:421` and `:480` guard nested package-root/framework/dependency/version/central kinds. Active assets authority is at `ProjectMetadataParser.cs:69`; redaction is at `WorkspaceForkApplyService.cs:829`. Platform identity and phase cancellation remain in the shared planner. The serialized cohort and supported hosted checks passed. | No behavior transfer. Distinct parser exception classification, inactive conditions and evaluator lifetime findings remain independent. |
| 5 | Observe original caller/kind regressions and real mixed-root assets rewrite fail before repair; add real cross-project producer tests comparing reviewed project-file effects to actual fork/source graphs, source bytes/version/token preservation and later source redemption. Preserve apply/undo, rebase and diff completeness regressions. | **PARTIAL.** Landed caller, kind, layout, dependency inventory, rebase, UTF-16 and physical undo regressions preserve genuine old-behavior receipts. The complete existing `CrossProjectRefactoringIntegrationTests` class ran in the qualified 100-class producer. Unmerged `WorkspaceForkReviewedGraphTests` is absent from main. | **Graph A4:** real public move, extract-interface and dependency-inversion fork/source comparisons; later source redemption; uniform/mixed/missing roots; ownership; cancellation/deadline; reload; expiry; no-copy refusal; failure rollback; preservation of landed apply/undo/rebase/diff cases. |
| 6 | Freshly review the complete scope or an independently correct dependency-chained split; run the required serialized full producer and hosted Windows/Linux/SDK-floor gates before closure. Scoped/source checkpoint proof alone never closes this row. | **PARTIAL; delivered scope qualified.** Thirteen child implementations are merged. Candidate `0707b3e6` passed the required hosted checks and equals merged main's tree. The complete delivered-scope producer passed across 100 exact classes with zero individual overlaps. One full `just ci` follows trailing documentation on final merged main. | **Graph A5:** fresh plan/implementation reviews, complete serialized producer and supported hosted Windows/Linux/SDK-floor qualification for the unmerged graph contract. Delivered-scope gates cannot qualify that contract. |

### Revised graph acceptance mapping

| Graph bullet | Preserved open requirement |
|---|---|
| A1 | Capture source nullable roots before copying exclusions; shared complete restore plan, platform identity, uniform/mixed scheduling and one continuing budget. |
| A2 | Rebase representable previews onto current state; prepare shared physical effects before copy; preserve siblings and refuse concrete unrepresentable snapshots. |
| A3 | Replay intended project/document effects and verify native contexts, reference/item metadata and Compile inventory before restore; preserve source and Undo authority. |
| A4 | Real public producer comparisons and root/rebase/ownership/cancellation/deadline/reload/expiry/refusal/rollback regression coverage. |
| A5 | Independent reviews, full serialized producer and supported hosted qualification; no closure from partial green checkpoints. |

The graph row is independent and nonblocking relative to this finite parent closeout. That disposition does not establish that every prerequisite of future graph work is satisfied.

## Validation record

| Required evidence | Result / owner | Claim boundary |
|---|---|---|
| Complete serialized affected producer | 1,752 passed; 0 failed; 3 skipped; 1,755 total; 100 exact classes; 45m22s | Actual TRX independently recomputed: zero individual/cross-class overlaps, exact class/filter equality and unchanged clean source head. This covers the delivered source/fork preservation cohort, not unmerged graph code. |
| Actual platform skips | Two Linux file-identity cases and one Linux case-sensitive executable-path case | Exact messages match guards at `ExternalEditStalenessTests.cs:309`, `:347` and `WorkspaceCloseDrainTests.cs:180`. TRX ancillary `notExecuted=0` is inconsistent with its three skipped leaves; `total-executed=3` agrees with actual leaves. All three skips are reported explicitly. |
| Producer category contract | `(exact class OR predicate)&TestCategory!=Benchmark&TestCategory!=Network` | One declared Network method is outside the PR lane: `WorkspaceLoadRestoreRaceTests.ReloadAsync_WithAutoRestore_ClearsRestoreRequiredAfterPackageVersionEdit`; zero selected Benchmark cases. No other functional cases are excluded. |
| Validation order | Serialized producer and supported hosted checks before closure; one full gate after trailing docs | Canonical remediation and ship validation cadence require one final clean merged-main `just ci`. |
| Hosted supported configurations | `SUCCESS` on `0707b3e6`; identical merged tree | Four Windows legs, two Linux legs, exact SDK floor, CodeQL and aggregate check. |
| Final documentation PR | PR1825; exact reviewed head and checks in its description | Documentation/backlog checks, cold review and hosted documentation topology must pass before merge. |
| Final clean merged-main gate after documentation | Root-owned; final receipt in PR1825 description | Run `just machine-validate ci`, preserving underlying `just ci`, on exact clean final merged main. Record SHA, exit and totals before declaring batch completion. This report does not pre-claim that later result. |

### Inherited child-gate ordering

| Recorded requirement | Current treatment |
|---|---|
| `fork-evaluated-source-isolation` acceptance 5 required the "serialized just ci delivery gate (CI_POLICY.md) before closure." | This inherited row ordering is superseded by the current canonical remediation/ship validation cadence: one full gate on final clean merged main after trailing documentation. The earlier literal gate has not been run or claimed satisfied. |
| Delivered isolation qualification | Independent review, the full CrossProject producer class inside the 100-class single-worker run, actual native regressions and supported hosted checks are qualified. The 100-class run is not literal `just ci`. |
| Remaining mandatory full gate | Root must run every `just ci` stage after this documentation PR merges, record its exact-head result in PR1825, and withhold batch completion until it succeeds. |

## Qualified child-row closures

Per-bullet source review, verified merges, the complete serialized producer and supported hosted checks qualify the following delivered rows. Canonical closures are part of PR1825 and take effect on main after its verified merge.

| Row | Delivery PR | Verified merge |
|---|---|---|
| `restore-physical-project-mutation-rendering` | 1808 | `6b93dcd632a477d9686c43a41596b32d75a64ee2` |
| `preview-project-reference-metadata-rebase` | 1811 | `fb978941fd0704b180884d00fb027b2e92f0cfe1` |
| `project-reference-utf16-undo-qualification` | 1814 | `b1d2bdcb62009f9ad651fae5489cf460341f3b18` |
| `restore-assets-json-kind-guards` | 1815 | `26610376ae408dfa20b6e485754e46b6e465c041` |
| `restore-active-assets-layout-authority` | 1819 | `f9a086e55358922b6d3db6f43a73ede358b9e7d5` |
| `fork-document-text-encoding` | 1820 | `7853c2937fb2e89d8a5bcf728e2ea2ab4d58debf` |
| `restore-required-project-inventory` | 1822 | `dad299cfbd986486309bbf1ab76a8918a7f3dcd6` |
| `fork-evaluated-source-isolation` | 1823 | `c3274dcead5abc69d6330022335f1126eb3a18e1` |
| `restore-metadata-evaluation-global-context` | 1818 | `1f4b9dccefc9c667046fce6cf03283c4cc094a0e` |
| `workspace-reload-preserve-global-properties` | 1817 | `e094ab38958169120ce62a1a01029a28d852b8ff` |
| `undo-explicit-empty-capture` | 1812 | `81f305586ba495bdce1252545ee0a1071509d103` |
| `fork-source-session-retention` | 1824 | `14fbdc025d5b767e568b9598936b6bb783add555` |
| `document-set-project-file-undo-capture` | 1828 | `7f877e84d76eaead701a5e1bd33d297f21ac1497` |

| Keep open | Reason |
|---|---|
| `project-reference-reload-embed-metadata` | Native MSBuildWorkspaces 5.9.0 loader still has no delivered evaluated `EmbedInteropTypes` repair. Snapshot rebase tests do not qualify initial load or reload. |
| `restore-fork-reviewed-project-graph` | Receives the explicit original acceptance transfer above; implementation and qualification remain incomplete. |
| Native-default/source encoding/reload and other independent follow-ups | Distinct mechanisms are recorded below; no claim of implementation is made. |

## Independent follow-ups in documentation checkpoint 74923ee8

The reconstruction checkpoint against main `7f877e84` preserved 15 new items, three updated items and the backlog index. At that stage, 336 unrelated rows and all recorded fields were preserved; subsequent qualified closures and the explicit graph transfer are separately reviewed changes in this PR.

| New row ID | Open mechanism |
|---|---|
| `document-set-native-default-compile-policy` | Effective native default-item policy during source document-set persistence. |
| `fork-expiry-cross-process-active-work` | Reproduce or disprove peer admission versus expiry race; retain existing persistent execution protections. |
| `fork-source-fixture-cleanup-failure-propagation` | Preserve failures and complete owned fixture cleanup. |
| `fork-source-fixture-initialization-cleanup-ownership` | Preserve cleanup ownership when fixture initialization fails. |
| `msbuild-evaluation-project-context-cache` | Actual loaded evaluation context and same-file context cache identity. |
| `msbuild-metadata-evaluation-collection-disposal` | Dispose the throwaway metadata evaluation collection; no measured leak claim. |
| `process-pipe-worker-probe-cleanup-ownership` | Complete owned probe cleanup despite release-write failures and bounded-wait gaps. |
| `restore-drift-inactive-package-reference-conditions` | Active evaluated package expectations rather than raw inactive conditions. |
| `test-workspace-unwatch-barrier-timeout-assertion` | Surface watcher fixture barrier expiry. |
| `testbase-workspace-cap-comment-drift` | Correct stale production-cap and fixture ownership comments. |
| `workspace-cache-fixture-cleanup-failure-silent` | Surface unexpected private cache fixture cleanup failures. |
| `workspace-explicit-reload-double-transition` | One explicit stale reload must not execute the automatic refresh first. |
| `workspace-load-shutdown-publication-race` | Independent load publication versus terminal manager shutdown. |
| `workspace-native-reference-global-context-mismatch` | Investigate native/reference/solution configuration versus actual loaded project membership. |
| `workspace-provider-retirement-shutdown-order` | Reproduce or disprove provider teardown versus manager-owned retirement ordering. |

| Updated existing row | Preserved scope |
|---|---|
| `new-file-apply-writes-bom-ignoring-editorconfig` | Add the observed null `SourceText.Encoding` fallback/preamble mechanism; retain original charset and line-ending acceptance. |
| `timing-test-reaudit-wave` | Preserve timing evidence and limitations; no threshold reduction or unqualified new steady-state claim. |
| `reserved-row-remediation-route-guidance` | Include current stale global prompt pointers; preserve historical references and existing active-route validation scope. |
| `restore-fork-reviewed-project-graph` — separate scope transfer | Preserve original history and every transferred graph clause, reload/expiry and protected references. Declared L requires decomposition before future implementation; five current production anchors and the unmerged helper are an investigation boundary. Existing merged dependency IDs remain historical references. |

The cancellation fixture header allegation is not a new row: its wording describes historical private helpers that were non-cancellable. Current internal visibility alone does not disprove that history.

## Proposed PR decisions and preservation

These are proposed decisions, not executed actions.

| PR | Proposed decision | Preservation and accuracy |
|---|---|---|
| 1826, draft head `0062d60ca47a0b08d56d1c91feac333b67de876a` | Close the incomplete bundled delivery proposal after the independent graph row is updated. | Preserve branch, dirty worktree, all 15 frozen handoff files, raw receipts and unmerged tests. Do not archive the worktree or claim the implementation merged. Existing encoding normalization and explicit reload corrections remain reference work. |
| 1809, head `b88d8c180dd057224da32aa24fe44b91dafb5420` | Close the superseded delivery vehicle after the delivered chain and open remainder are linked. | Superseded vehicle does not mean every old hunk landed. Preserve branch/worktree and protected stash. Also preserve partial extraction `codex/restore-callers-complete` at `450c75dd61611e5c4aa0bfa3fbeccdcb3d224299` and its separate dirty work. |
| 1806, draft head `d333c2e96a1e23a31286cda53b920dbee5d6b7be` | Retain as an unapproved wider architecture proposal requiring a future product decision. | Its unmerged ADR is **`docs/decisions/0029-explicit-preview-snapshot-authority.md`**. Main already has the different **`docs/decisions/0029-fork-source-session-lifecycle.md`**, accepted for the next major release. Retention does not approve the wider architecture or make it a restore prerequisite. Proposed owned BuildHost evidence is scratch evidence, not a tracked deployed implementation. |
| 1825, current draft head `74923ee898d6e58bede402b309f293a1b94a09c4` | Finish the canonical graph transfer, gate-backed closures and report; obtain cold review and documentation checks, then land. | Preserve reconstructed rows/history and all unrelated rows. Final head and merge remain pending. |

| Protected reference | Required disposition |
|---|---|
| `origin/codex/restore-reviewed-graph` / PR1806 | Preserve as wider unapproved reference. |
| `codex-remediate-20261022-preserve-restore` stash `ba3f29984824b9f302db1c1ff66e6a890e33762c` | Preserve; never pop or drop during this closeout. |
| PR1809 and partial extraction `450c75dd` | Preserve source work independently of closing the old PR vehicle. |
| PR1826 dirty work and frozen handoff | Preserve exact work and future acceptance; do not copy code or start another implementation. |

## Final qualification ownership

| Owner | Required action |
|---|---|
| Cold reader | Review the final report, canonical row updates/deletions and disposition wording before merge. |
| Root | Verify PR1825 merge; execute and verify the stated PR dispositions without deleting protected work. |
| Root | Run and record final clean merged-main `just ci` in the PR1825 description, then report the result and end the finite run. |
