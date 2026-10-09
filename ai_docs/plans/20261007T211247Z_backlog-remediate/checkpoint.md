# Planning checkpoint — 20261007T211247Z_backlog-remediate

## Operator update 2026-10-09

| Item | Disposition |
|---|---|
| Authorization | File global backlog rows only; global implementation belongs to another agent. Complete eligible product work; defer product work requiring the missing tooling. |
| Global prerequisites | Fanout guidance: claude-config PR #718. Indivisible Rule 5 admission: claude-config PR #724, row `plan-rule5-indivisible-scope-admission`. Current exec-args reproduced exit 4 for 110000 > 80000; no bypass. |
| Deferred | `preview-store-explicit-internal-state`, `fork-composite-nonconsuming-snapshot`, `fork-preview-project-composite-replay`. Preserve full scope and open backlog rows; resume only after tooling correction, source re-vet and fresh cold review. |
| Admission | Fresh cold plan review passed with warnings; hashes verified. Plan PR #1762 merged at e52c124196c350a334e30b5b925274f3e9974b46. Main backstop run 37951698771 succeeded. |
| Eligible remainder | 24 pending initiatives. Navigation merged and reconciled; workspace lifecycle merged, reconciliation pending. Three tooling-dependent initiatives deferred; one notice initiative obsolete and reconciled. Plan remains incomplete. |
| Navigation implementation | PR #1764 merged at 5cf87d77d2d6709d6c92b1b7f1c6af96139792e5, 2026-10-09T17:13:21Z. Fresh full producer 3bf9ee18ccac6b02 passed tree 578e6d25d26313c3ab0733cc795f90b39808093a: 3982 passed, 12 platform skips, zero failures. Cold re-review passed zero findings; hosted checks green. Main backstop run 37964842895 succeeded on exact merge commit. |
| Navigation cleanup | Implementation worktree removed; primary main fast-forwarded. Scratch cache quarantined after EPERM; holder probe returned only CLIXML framing. Underlying holder unconfirmed; cleanup remains incomplete. |
| Other global filings | Existing active-conflict-cache row updated in PR #727; existing Windows Bash-resolution row updated in PR #729; holder-probe CLIXML diagnostics filed in PR #733. Filing only; no global implementation. |
| Additional tracked debt | Low/S symbol-resolver-token-lookup-comment-parity: unchanged stale findInsideTrivia comment; actual exact-position and lenient preceding-token lookups verified. |
| Lifecycle implementation | PR #1766 merged at 3633ca4c819893d840a6a680857101c68f35b55a, 2026-10-09T18:54:11Z. Cold review passed zero findings and hosted checks green on 69335f4d583807533193e88f3db712710a68d3bb. Full producer 1ea70ed7d1abbacd passed exact tree fb1d29f179e7eaecde33001a4d1f4e07866539b0: 3995 passed, 12 platform skips, zero failures; merge tree equality verified. Main backstop run 37976541569 succeeded on exact merge commit. |
| Lifecycle cleanup | Implementation worktree removed; primary fast-forwarded. Scratch cache quarantined after EPERM with unknown holder and CLIXML-only diagnostic; existing global PR #733 tracks diagnostic repair. No cleanup success or lock root cause claimed. |
| Security follow-up | Medium/S workspace-msbuild-global-properties-value-redaction filed after confirming unchanged loader Information logging of arbitrary caller property values on immutable base. One production logging site; no changes to active executor scope. |
| Reconciliation | PR #1765 merged at d9ae7d6c12535e0d2ed3f87c4d68a1c4a0af8643, 2026-10-09T18:01:57Z. Cold cycle 2 passed with zero findings; hosted checks green. Stale derived hashes/cache repaired through normal hash and generations --persist; existing global row stanza-amend-derived-file-state-stale owns prevention. Machine-specific evidence path corrected through backlog writer; local docs gate passed. |
| Resume | Preserve current plan branch/worktree; await lifecycle main backstop then reconcile before next dispatch. Generation base remains d9ae7d6c12535e0d2ed3f87c4d68a1c4a0af8643 until verified reconciliation recaptures it. Two of ten default implementation dispatches used; contextPct unavailable. Navigation dispatch 2026-10-09T15:25:04Z, reconciliation 18:01:57Z; 156.9 minutes, contextCost unavailable. Lifecycle brief prepared 18:03:24Z; final reconciliation/minutes pending. |

## Historical checkpoint before plan admission

The sections below record the earlier unmerged checkpoint. Current status is the operator update above plus helper-owned state; older next actions and snapshots are superseded.

## Landed

| Item | Evidence |
|---|---|
| Implementations | None. No executor dispatched; no implementation PR or merge. |
| Primary | main and origin/main verified at 6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4; clean; no local commits ahead. |
| Plan | Existing unmerged PR #1762, chore/plan-20261007T211247Z_backlog-remediate. This checkpoint saves planning work only. |

## Gated but unlanded

| Item | Current-session evidence / limits |
|---|---|
| Preflight | addenda-lint: zero errors and warnings; GitHub authentication succeeds. |
| Documentation | verify-ai-docs passed after the dependency-stage split, including package parity, third-party notices and 38 shipped Markdown files. No full implementation gate was run. |
| Backlog | 341 rows; zero errors, four existing warnings; two new focused rows have no introduced row warnings. |
| Cold review | Cycle 0 failed: four blocks, five warnings, fourteen info. Every pending initiative reviewed; graph matched, dependencies acyclic. Cycle 1 failed: two unresolved safety blocks, eight scheduling warnings, twenty info; all 29 pending initiatives and whole graph reviewed. Blocks decreased from four to two; no new block. Read review.md and live state. No passing review/hash or executable admission is claimed. |
| Notice row | One initiative remains previously marked obsolete. Prior checkpoint's 3/3 test claim is historical; closure needs current-source/evidence verification during reconciliation. |
| Global filing | PR darylmcd/claude-config#718 tracks conflicting fanout instructions; exact pushed head 96cdaa46aa549b1ae17d913c0a1df7ea9122f42c. Row size corrected to M; no global tooling implementation. Filing worktree/local branch removed after verified push; remote PR remains open. |

## Next actions

1. Resume this unmerged plan from its remote plan branch, never origin/main; inspect latest against the plan worktree. Re-derive live PR/head, review, backlog and worktree claims.
2. Resolve preview-store-explicit-internal-state before executable admission. Full scope remains 38 production / 26 test files, estimated 110000. Canonical Rule 5 marker is 80000; exec-args mechanically refuses the larger estimate. Required return/store metadata must reach all solution/project/composite producers and apply guards. Proposed safety stages were NOT admitted: changing shared generation forces project/composite callers before their storage changes, so independent correctness has not been established. Do not lower estimates, drop callers, discard required state or add compatibility defaults.
3. Operator decision: authorize an audited workflow change supporting this specifically justified atomic migration, or explicitly hold this initiative while executing independently reviewed rows. Recommendation preserves the complete root-cause repair; no weakened intermediate. Global instruction typo is tracked separately; its canonical-rule resolution does not waive Rule 5.
4. Token lifecycle now has five in-plan dependency stages; fork support has two, and depends on the complete shared safety initiative. Re-vet every stage on execution base. ADR reservations: 0020 safety, 0021 cross-project refusals, 0022 owned process drain, 0024 token lifecycle, 0025 isolated fork replay.
5. Once the final pending artifact has passing cold review, record review via helper, set anchor verification, refresh graph and hash, then land the plan with required hosted gates and a final exact-head review. No implementation before plan admission/merge.
6. Arm drain-guard only when the primary plan exists and execution is authorized. Serialize executors/full gates per addenda. No contextPct was supplied: at most ten implementation dispatches before checkpoint; this resume dispatched zero.
7. Reconcile only proven merged/obsolete rows through the reviewed metadata flow; retain operator/native-ownership investigations until evidence supports closure. Delete checkpoint at verified final closeout.

## Initiative snapshot

| Initiative | Status | Stanza/deepener | Production / tests |
|---|---|---|---|
| navigation-and-locator-argument-refusals-public-message | pending | ok | 7 / 6 |
| workspace-lifecycle-argument-refusals-public-message | pending | ok | 4 / 1 |
| tool-refusal-public-message-guard | pending | ok | 2 / 3 |
| extract-type-preserve-untouched-trivia | pending | ok | 1 / 1 |
| scaffold-batch-preview-apply-route | pending | ok | 6 / 3 |
| fork-composite-nonconsuming-snapshot | pending |  | 4 / 2 |
| fork-preview-project-composite-replay | pending |  | 2 / 1 |
| preview-token-lifecycle-evidence | pending |  | 6 / 9 |
| preview-token-reason-projection | pending |  | 8 / 4 |
| preview-token-solution-confirmation | pending |  | 1 / 3 |
| preview-token-composite-confirmation | pending |  | 3 / 3 |
| preview-token-project-confirmation | pending |  | 1 / 2 |
| notice-verifier-scoped-test-inherits-nuget-packages | obsolete | pending | 0 / 0 |
| test-discovery-file-path-case-identity | pending | ok | 1 / 1 |
| workspace-project-alias-lookup | pending | ok | 1 / 1 |
| fix-all-equivalence-key-unregistered-fallback | pending | ok | 1 / 2 |
| editorconfig-section-source-path-matching | pending | ok | 2 / 1 |
| logging-jsonl-drops-structured-state | pending | ok | 2 / 2 |
| restore-callers-missing-packages-path | pending | ok | 5 / 4 |
| split-service-refuse-cross-partition-references | pending | ok | 3 / 1 |
| change-signature-class-struct-primary-ctor-add-remove | pending | ok | 2 / 1 |
| load-profile-full-suite | pending | ok | 3 / 4 |
| workspace-close-global-build-server-shutdown | pending | ok | 4 / 3 |
| test-temp-reaper-live-owner | pending | ok | 0 / 2 |
| change-signature-service-refusals-public-message | pending | ok | 1 / 3 |
| cross-project-public-refusals-echo-input | pending | ok | 1 / 2 |
| top10-good-preview-result-contract | pending | ok | 0 / 1 |
| workspace-close-isolated-nuget-handles | pending | ok | 1 / 1 |
| preview-store-explicit-internal-state | pending | ok | 38 / 26 |
| preview-diff-whitespace-omission | pending | ok | 4 / 4 |

## Deviations

| Item | Disposition |
|---|---|
| Stop reason | Operator scope/workflow decision; not a completion claim. Drain guard disarmed. |
| Selection | Existing plan resumed; no new selection or count normalization. 30 in-plan initiatives after review-remediation splits: 29 pending, one previously obsolete. Original queued refusal children remain for a later selection. |
| Planning fixes | Completed seven missing stanzas; verified shared diff consumers; count row-detail edits as production; superseded blanket split-service refusal with source-owned semantic composition; reserved distinct ADR ids and decision-index companions. |
| Safety split proposals | Preserved outside repo at the session scratchpad; NOT applied because independent generation migration is unproved. Parent remains held for correct atomic scope or a verified independent decomposition. |
| MCP | Deferred live Roslyn heartbeat/load/navigation/reference probes succeeded. Root workspace loaded six projects/950 documents with missing analyzer-output readiness diagnostic. One lifecycle deepener load failed with InternalError correlationId d3afe52ea24640bb983d90d717c082c7 and used verified text fallback. No compile/test success is claimed. |

## Spin-off sketches

| Row | Source-derived tracking |
|---|---|
| change-signature-callsite-update-path-identity | Filed Medium S: ChangeSignatureAddRemovePreviewBuilder.cs:20 groups case-distinct Linux paths with OrdinalIgnoreCase; :170 accumulates counts while changed files remain DocumentId-distinct. No runtime regression yet. |
| composite-apply-platform-path-identity | Filed Medium M: CompositeApplyOrchestrator.cs:95,167,182 collapses undo/applied paths; UndoService.cs:194 uses the same insensitive identity for overlap. No runtime regression yet. |
| plan-deepener-fanout-flag-hardens-advisory-count | Filed globally in PR718: plan-deepener.md:73 says both flags above ten; authoritative rules:127 requires heroic-last only; rubric:167 blocks the extra fanoutOversize flag. No helper bypass or global code edit. |
| Existing tracked defects | Whitespace preview omission, age-only temp reaping, global build-server shutdown, unsafe public refusals and dropped structured log fields stay in this plan. Prior provider-failure swallowing and editorconfig writer precedence rows remain open. |
| Unproved concern | Test fixture generated-directory case matching may be deliberate conservative exclusion; no concrete incorrectly skipped fixture found, so no bug claim or row filed. |

## Self-review

- Task remains incomplete: planning advanced; no implementation shipped.
- Evidence: current Git/PR checks, cold cycle-0 review, sanctioned mutations, clean primary, AI-doc gate and backlog lint.
- Fragile points: atomic preview safety versus review-size policy; exact new lifecycle/snapshot contracts still need implementation regression evidence.
- Cold review governs admission; no red/held artifact is marked executable.

