# Planning checkpoint — 20261007T211247Z_backlog-remediate

## Landed

| Item | Evidence |
|---|---|
| Implementations | None; no executor dispatched and no implementation PR created. |
| Primary base | main = origin/main = 6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4 at selection. Re-fetch on resume. |
| Plan | Unmerged PR https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1762; branch chore/plan-20261007T211247Z_backlog-remediate. Initial skeleton 05ad022d97992a6b64c1d7197086e3e35a0b3877; this checkpoint commit follows it. |

## Gated but unlanded

| Item | Evidence / limits |
|---|---|
| Plan PR #1762 | Planning incomplete; reviewStatus pending, readyForExecute false. No integration tree OID or implementation gate exists. |
| notice-verifier-scoped-test-inherits-nuget-packages | Obsolete in state only. Current ThirdPartyNoticeDriftTests.cs:132-138,237 already supplies active package root; current-session Roslyn test_run passed 3/3, 0 skipped, exit 0, 26440 ms. Backlog closure awaits reviewed reconciliation. |
| Preflight | addenda-lint 0 errors / 0 warnings; initial backlog-lint 335 rows, 0 errors / 4 warnings. Post-mutation backlog lint: 339 rows, 0 errors / 4 existing warnings, 73 informational dependency notes; no new/touched-row warnings. AI docs verification passed including package parity, third-party notices and 38 shipped markdown skills; no full implementation gate claimed. |

## Next actions

1. Read canonical backlog-remediate resume protocol; re-derive live git/PR claims. Resume the existing plan worktree/remote plan branch. Never recreate the unmerged plan from origin/main. Run latest against the plan worktree; the primary has no merged copy yet.
2. Finish stanzas for: logging-jsonl-drops-structured-state, workspace-close-global-build-server-shutdown, test-temp-reaper-live-owner, change-signature-service-refusals-public-message, cross-project-public-refusals-echo-input, workspace-close-isolated-nuget-handles, preview-diff-whitespace-omission. Logging needs a deepen-route decision for structured-state serialization/privacy; do not treat its provisional direct classification as proof.
3. Reconcile preview-store-explicit-internal-state (38 production / 26 tests), preview-token-consumed-reason (20 / 14) and workspace-fork-project-mutation-preview (13 / 8) into independently complete dependency stages. Share actual completeness/lifecycle prerequisites; avoid duplicate competing contracts. Proposals are unreviewed and must not execute unsplit.
4. Amend split-service-refuse-cross-partition-references detail through backlog.mjs to supersede blanket refusal with verified semantic ownership/composition. Re-vet primary-constructor shared binding repair, restore mixed-root planning and headless profiling designs; source claims do not constitute approval. Expand/re-measure editorconfig matcher corpus as its Evidence requires. Writer-effectiveness debt is separately tracked.
5. Cold whole-plan adversarial review must consume this exact plan/ directory, state, backlog, addenda and canonical rules. Resolve/stamp passing review via helper, refresh generations and hash evidence, then land plan PR with required gates. No passing review/hash stamp currently exists.
6. After plan merges, arm drain-guard on primary plan with live worktree state; admit implementation serially under serializeFullCi. Generate canonical executor/reviewer briefs, prove regressions on old code, scoped checks and exact-head cold review, then required hosted checks before landing/reconciliation. No contextPct supplied: at most 10 implementation dispatches before another checkpoint unless a real signal arrives.
7. Reconcile obsolete row and any implementations only with reviewed evidence. Delete checkpoint in final reviewed reconciliation; refresh completed snapshot and prove closeout/clean main.

| Initiative | Status | Stanza/deepener | Production / tests |
|---|---|---|---|
| navigation-and-locator-argument-refusals-public-message | pending | ok | 7 / 6 |
| workspace-lifecycle-argument-refusals-public-message | pending | ok | 4 / 1 |
| tool-refusal-public-message-guard | pending | ok | 2 / 3 |
| extract-type-preserve-untouched-trivia | pending | ok | 1 / 1 |
| scaffold-batch-preview-apply-route | pending | ok | 6 / 3 |
| workspace-fork-project-mutation-preview | pending | ok | 13 / 8 |
| preview-token-consumed-reason | pending | ok | 20 / 14 |
| notice-verifier-scoped-test-inherits-nuget-packages | obsolete | pending | unset / unset |
| test-discovery-file-path-case-identity | pending | ok | 1 / 1 |
| workspace-project-alias-lookup | pending | ok | 1 / 1 |
| fix-all-equivalence-key-unregistered-fallback | pending | ok | 1 / 2 |
| editorconfig-section-source-path-matching | pending | ok | 1 / 1 |
| logging-jsonl-drops-structured-state | pending | pending | unset / unset |
| restore-callers-missing-packages-path | pending | ok | 5 / 4 |
| split-service-refuse-cross-partition-references | pending | ok | 2 / 1 |
| change-signature-class-struct-primary-ctor-add-remove | pending | ok | 2 / 1 |
| load-profile-full-suite | pending | ok | 3 / 4 |
| workspace-close-global-build-server-shutdown | pending | pending | unset / unset |
| test-temp-reaper-live-owner | pending | pending | unset / unset |
| change-signature-service-refusals-public-message | pending | pending | unset / unset |
| cross-project-public-refusals-echo-input | pending | pending | unset / unset |
| top10-good-preview-result-contract | pending | ok | 0 / 1 |
| workspace-close-isolated-nuget-handles | pending | pending | unset / unset |
| preview-store-explicit-internal-state | pending | ok | 38 / 26 |
| preview-diff-whitespace-omission | pending | pending | unset / unset |

Planning envelopes and input contracts remain outside the repository at <session-scratchpad>/20261007T211247Z_backlog-remediate/deepener-<id>.result and input-deepener-<id>.json. Current merged stanzas preserve their returned diagnoses; no executor should rely on scratch prose without re-vetting the committed stanza. selection.json records exact selection and skip classifications.

## Deviations

| Item | Decision / remaining obligation |
|---|---|
| Stop trigger | Actual context compaction. Canonical context rule: checkpoint at first clean boundary after compaction; no new agents/executors dispatched after it. |
| Selection | No literal count. First 40 routing window from 254 re-vetted eligible; retained one split, skipped 14 additional splits without backfill; 26 initial selected, now 25 state initiatives after route split; 214 beyond ceiling. No global backlog completion claim. |
| Route split | Closed planning parent code-action-and-flow-argument-refusals-public-message through blessed writer; queued children code-action-index-public-refusal, flow-region-public-argument-refusals, fix-all-scope-public-argument-refusals for later selection. Updated two dependent rows to require all children. Parent retirement is not shipped repair. |
| Correctness widening | Navigation includes all unchecked coordinates; primary constructor includes shared semantic argument binding; restore includes source-root capture/mixed roots/JSON-shape guards; split-service replaces representable blanket refusals with source-owned composition; preview rows cover per-file/aggregate state and actual apply success. Review all before admission. |
| Scheduling | Full gates serialize; scoped checks do not replace full required validation. Large fanout needs reviewed dependency decomposition, not omissions or false default metadata. |
| MCP | Deferred discovery/live workspace bootstrap succeeded. Workspace IDs are connection-local; each agent loads concrete absolute RoslynMcp.slnx. |

## Spin-off sketches

| Row | Evidence / disposition |
|---|---|
| code-fix-provider-failure-silent-fallback | Filed Medium S. RefactoringService.cs:811-817 catches provider failure and returns null, masking failure as no action/legacy fallback. Distinct from FixAll registered-key selection; remains open. |
| editorconfig-writer-effective-section-precedence | Filed Medium S. EditorConfigService.cs:510-525 changes first existing assignment and returns even when later applicable section overrides it. Distinct from reader matching; remains open. |
| Existing documentation debt | stale-pre-apply-hook-consumer-docs and argument-error-retired-family-design-links already track observed stale references; re-vet before any closure. |
| Global writer defect | Existing global row backlog-update-final-section-eof tracks replaceDetailSection appending an extra blank line at EOF (current helper line508). Supported whole-detail update corrects this checkpoint's formatting. Duplicate filing PR https://github.com/darylmcd/claude-config/pull/714 was closed and its remote branch deleted after existing tracking was confirmed; no global tool implementation changed. |
| Own/exposed defects | BoundedStore max+1 capacity and hardcoded false truncation in EditService.cs:304-310 / FixAllService.cs:276-279 stay inside complete preview/lifecycle implementation; do not defer them to unrelated rows. |
