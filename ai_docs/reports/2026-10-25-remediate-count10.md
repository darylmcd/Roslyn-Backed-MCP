# Error-classification remediation

<!-- scope: in-repo -->
<!-- purpose: Record selected scope, independently reviewed delivery evidence, exclusions and retained work. -->

| Contract | Disposition |
|---|---|
| Request | `/remediate count=10`; ceiling of ten, three complete failure-classification items selected and merged. Final batch qualification follows all trailing documentation. |
| Starting main | `7688a4e51ed678117cc3d0c4ee01cf2668125df9`; fetched, clean and synchronized. |
| Final qualification | After this final documentation merges, root runs `just machine-validate ci` on exact clean merged main. Store immutable head, original exit status and totals in the final closeout PR description; scoped receipts below do not substitute for it. |
| Backlog authority | All additions, evidence corrections and closures use `~/.claude/scripts/backlog.mjs`; no hand-edited index or item files. Close selected rows only after their code PR merges. |
| Roslyn | Deferred `mcp__roslyn__` discovery and live probes succeeded. Load each worktree's absolute `RoslynMcp.slnx`; restore/build analyzer outputs before claiming readiness. |

## Selected delivery

| Row | Root cause and complete change | Review and runtime evidence | Delivery |
|---|---|---|---|
| `code-action-index-public-refusal` | The sole plain argument throw discarded safe action-index/count guidance. Use the existing public carrier; preserve `InvalidArgument` and wire `ArgumentException`. | Cold plan and implementation PASS. Corrected causal baseline: 8 failures at exact message assertion; final clean-commit suite: 49 passed, 0 failed/skipped. Compile check: 8/8 projects, 0 errors. Both protocol eras and valid previews exercised. | [PR1833](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1833), merged `c8c05f23226d363b817bdbf8e3aa38dc18298cf4`. Reviewed `c7080eba` has identical Git tree. All hosted Windows/Linux/SDK-floor and CodeQL checks passed; cleanup exit 0. |
| `tool-error-handler-lineRange-sentinels-retired` | All matching production throwers already use public carriers. Delete the two dead classifier heuristics; preserve typed producer guidance and generic unmarked fallback. | Cold plan and implementation PASS. Baseline: 3 new classifier failures, 26 preservation passes. Final integrated suite: 175 passed, 0 failed/skipped, all 12 affected classes present. Compile check: 8/8 projects, 0 errors. Source-text tool cases extended to both protocol eras. | [PR1834](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1834), merged `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Reviewed `f2c5c469` has identical Git tree. All hosted Windows/Linux/SDK-floor and CodeQL checks passed; cleanup exit 0. |
| `project-metadata-parser-broad-exception-fallbacks` | Narrow three unconditional fallbacks and the fourth assets-evaluation boundary. Restrict path exceptions to path operations; retain exact native MSBuild wrapper identity and deferred type loading. | Cold plan and implementation PASS. Causal baseline: 11 failures/5 passes. Final clean integrated suite: 166 passed, 0 failed/skipped, all 10 classes present including 22 new failure cases. Fresh-process malformed/missing-import probe: 2 passed. Full solution compile: 8/8 projects, 0 errors/warnings. Build, formatting, docs and fragment checks passed. | [PR1835](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1835), merged `4b90ca388c27b729613ce105e4b4fd4cd23d5e08`. Reviewed head `0525616808eb33fccd7dd539dc24b78cdf46833d` has identical source/test/fragment content; the merged tree also includes the approved checkpoint documentation. All six hosted legs, SDK floor and CodeQL passed; cleanup exit 0. |

## Same-mechanism coverage

| Item | Inspected construct set |
|---|---|
| Code-action index | Semantic action-index sweep and references identify one guard/throw, the host route, and three existing integration callers. Preserve exact carrier assertions and both route-binding classes. |
| Line-range classifier | Converted producers in `SourceTextRequestProjection`, `WorkspaceResources`, `FlowAnalysisService` and `RefactoringService`; no matching Core producer. The broader remaining classifier heuristics remain separately tracked. |
| Metadata fallback | `ProjectMetadataParser.GetExistingAssetsPath`, `GetEvaluatedTargetFrameworks`, `GetEvaluatedOutputType`, `LoadProjectDocument`; shared expected-evaluation policy in `MsBuildMetadataHelper`. The fourth boundary and helper are a forcing scope expansion, not optional adjacent cleanup. |

## Follow-ups and existing debt

| Row | Observed independent mechanism |
|---|---|
| `code-action-preview-invalid-operation-classification` | Missing-document correction and provider operation invariants need separate intentional classification. New Medium/S row; do not indiscriminately publish provider detail. |
| `invalid-operation-ratchet-retired-msbuild-allowance` | Constructor ratchet still permits an obsolete helper identity after the implementation moved to a factory. New Low/S row; no replacement allowance authorized. |
| `project-test-package-name-redundant-case-entry` | `NUnit` and `nunit` duplicate data in an ordinal-ignore-case set. New Low/S row; no runtime classification defect claimed. |
| `tool-error-handler-argument-sniffing-arms-retired` | Remaining unrelated classifier heuristics are already tracked; this batch does not retire the next-major root-boundary dependency. |
| `reserved-row-remediation-route-guidance` | Current workflow retains stale global prompt pointers; the existing row owns that routing cleanup. |
| Global `backlog-update-final-section-eof` | A sanctioned final-section update produced an extra EOF blank line; `git diff --check` reproduced it. Existing global tooling row owns the fix. One supported whole-item update normalized the generated detail; no tooling or guard was modified. |

## Excluded candidates

| Row or held family | Reason |
|---|---|
| `persistent-composite-workspace-identity-not-portable` | L lifecycle scope requires decomposition and independent ownership design before implementation. |
| `root-boundary-argument-refusals-public-message` | Explicit next-major category/release hold; policy approval does not authorize current minor delivery. |
| `timeout-error-names-fired-timer` | L multi-producer timer contract requires decomposition; not part of this bounded failure-classification cluster. |
| `test-run-nobuild-msbuild-properties` | L feature scope requires decomposition; existing command selection preserved. |
| `editorconfig-section-source-path-matching` | Reader-only repair is incomplete; preserve approved writer/global precedence obligations and re-plan the complete contract. |
| `other-failed-apply-displaces-undo-target` | L multi-producer undo ownership requires decomposition. |
| `client-root-widening-drive-root-parent` | Independent security-boundary mechanism; preserve the recorded prefix policy and its security qualification obligations. |
| `logging-retention-destroys-repro-evidence` | Nominal S row contains rotation, oversized-record and pruning regression shapes; decompose before code. |
| `logging-event-id-collisions-no-catalog` | L event-catalog scope needs decomposition. |
| `logging-failure-records-lack-diagnosis` | Separate observability-field/frame projection mechanism, outside the selected classifier fixes. |
| `type-consumer-not-found-public-guidance` | Live `KeyNotFoundException` contradicts stale InvalidOperation acceptance. Re-derive guidance while retaining the actual released category; do not close solely for stale wording. |
| `cross-project-public-refusals-echo-input` | Already-public sensitive-detail removal requires the recorded release-policy ADR/migration strategy. |
| Promotion and tool-consolidation children | Preserve top-level audit, ADR and open dependency requirements. |
| Defer/next-major families | Preserve explicit product/release holds. |

## Retained work and evidence limits

| Subject | Disposition |
|---|---|
| PR1806 | Source-only reconciliation accounts for all 794 paths, 55 divergences and 101 retained findings; 38 new owners preserve missing work and the held architecture decision. Publication and operator-authorized closure retain branch, immutable tag, old worktree and original stash. See [reconciliation](2026-10-25-pr1806-reconciliation.md); no original architecture or runtime qualification is implied. |
| Prior extraction, fork graph and paused correctness work | Preserve every pre-existing dirty worktree and retained reference. These selected independent fixes do not restart, overwrite or certify them. |
| Initial code-action experiment | Failed first at schema guidance; retain it, but use the corrected exact-message old-code experiment as causal message-loss proof. |
| Initial cold-start filter | Selected zero tests and exited nonzero. Corrected exact-method filter executed two fresh-process cases; only that successful run supplies cold-start evidence. |
| CI wait | PR1833/1834 and recovery PR1836/1835 each reached READY through the permitted second bounded watch. Only PR1836/1835 received one complete operator-authorized recovery rerun after observed upstream route recovery; no test retry loop. |
| Final status | At this documentation commit, final merged-main full qualification is pending. Use the final closeout PR description for the post-merge receipt; do not amend validated source solely to record it. |

## Recovered qualification hold

| Evidence | Result |
|---|---|
| Original PR1835 CI run | [Attempt 1](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/40223866909/attempts/1) failed all six matrix legs during SDK setup before tests; SDK10.0.403/runtime10.0.14 origin returned HTTP409. Preserve this failed evidence. |
| Base reproduction | [40224224815](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/40224224815), exact then-main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`, reproduced SDK/runtime HTTP409; isolated SDK floor passed. |
| Acquisition recovery | At `2026-10-25T20:33:38.241Z`, official aka.ms LTS runtime and 10.0 SDK aliases redirected to builds.dotnet.microsoft.com and returned HTTP200. The direct origin remained private/409; the operator explicitly approved the recovered official routes as the recovery condition. |
| Tracking | `hosted-dotnet-acquisition-origin-access-denied` acceptance is met and the row closes canonically here. [PR1836 attempt 2](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/40225081974/attempts/2) and [PR1835 attempt 2](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/40223866909/attempts/2) passed on unchanged heads. No SDK pin, workflow edit, alternate installer or check bypass. |
| Backlog balance | start=338; selected_closed=3; initial_rows_filed=4; reconciliation_rows_filed=38; docs_ci_rows_filed=1; acquisition_closed=1; open=377; lint_errors=0; lint_warnings=0. Resolve current owner status against the canonical backlog and delivery receipts; immutable audits retain their stated baseline. |
| Delivery order | Operator order observed: PR1836 merged first as `e1650ab298d2010790f74ad56a84b699793694c6`, PR1835 second as `4b90ca388c27b729613ce105e4b4fd4cd23d5e08`; independent PR1806 reconciliation precedes this small metadata/blocker closeout. Root runs the final full gate afterward and verifies current main, retained work and task cleanup. |
## Additional operator tracking

| Owner | Disposition |
|---|---|
| `docs-ci-sdk-download-dependency` | Open follow-up; remove docs-linux SDK-download dependence while preserving complete documentation tests, release checks and public-fork trust. No workflow implementation in this batch. |
| PR1806 unique content | [Reconciliation and row inventory](2026-10-25-pr1806-reconciliation.md) preserves all original paths and finding details under existing owners, current investigations or the Defer decision. Closing the PR vehicle does not adopt its architecture or complete extraction/paused15. |
| Additional peer worktree | Preserve `devbuild-runner-pilot` and every pre-existing worktree; task cleanup does not assert a globally single checkout. |
| Initial documentation profile-path failure | Original gate failed the report's machine-specific writer path. A one-line portable-path correction passed independent delta review and the corrected committed-head docs gate; retain the failed log. |
