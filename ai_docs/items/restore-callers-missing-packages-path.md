# restore-callers-missing-packages-path — Preserve recorded roots and the reviewed fork graph

**row:** `restore-callers-missing-packages-path` · **pri:** `Medium` · **size:** `L` · **deps:** `workspace-restore-packages-path`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:232`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:374`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:128-149`
- (new) `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:644-731`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:26`
- (new) `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs`
- (new) `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:830-868`
- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs:67-115`
- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:113`
- `tests/RoslynMcp.Tests/TUnitMtpNativeTestRunTests.cs`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestorePackagesPathTests.cs`
- `tests/RoslynMcp.Tests/RestoreStalenessDetectorTests.cs`
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`
- `tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs`
- `tests/RoslynMcp.Tests/PreviewStoreTests.cs`
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`
- (new) `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`
- `docs/decisions/0010-validation-verdict-completeness.md`

## Acceptance

- [ ] One shared required restore plan preserves each relevant project's recorded nullable package root and exact evaluation context with platform identity; nonempty graphs restore dependency-first per project with --no-dependencies and one total timeout/cancellation budget. Equal roots/globals do not authorize solution batching; coalesce shared assets only for a proven complete compatible outer framework set.
- [ ] Test-runner restore precedes resolved-version authorization and retains --no-restore test execution. Capture source assets before copying fork exclusions; verify actual copied/evaluated project graph and source isolation before restoring it.
- [ ] Ordinary accepted reference-changing previews retain intended graph changes and current sibling edits. Reuse shared physical mutation rendering for source apply, preview diffs/path visibility and isolated fork replay; preserve encoding, reference metadata, rollback and token authority. Concrete unrepresentable snapshots fail before copy or runner effects; do not blanket-refuse representable graph changes.
- [ ] Guard valid malformed JSON kinds at every assets reader access; retain alternate layouts, missing/default roots, Linux case identity, redaction and cancellation.
- [ ] Observe original caller/kind regressions and real mixed-root assets rewrite fail before repair; add real cross-project producer tests comparing reviewed project-file effects to actual fork/source graphs, source bytes/version/token preservation and later source redemption. Preserve apply/undo, rebase and diff completeness regressions.
- [ ] Freshly review the complete scope or an independently correct dependency-chained split; run the required serialized full producer and hosted Windows/Linux/SDK-floor gates before closure. Scoped/source checkpoint proof alone never closes this row.

## Evidence

| Snapshot | Evidence | Limit |
|---|---|---|
| Original diagnosis | Parent workspace-restore-packages-path deepener identified two restores omitting --packages. | Narrow original acceptance omitted actual graph correctness companions. |
| Base337fc2cde10eca3f591cc016c146dee7b893e26a | CrossProjectRefactoringService195/200/775 stores ordinary reference-changing preview; fork replays only documents. SolutionDiffHelper67-115 and PreviewStore211-252 omit csproj reference effects. Existing DocumentSetPersistenceService375-485 renders references but couples source application. | Source re-vet; graph repair unimplemented. |
| Source checkpoint ea969292d61a8c633cb77581a587841d36742741 | Exact5production/4tests/1fragment committed/pushed, no PR. Baseline9failed/22passed; legacy mixed-root semantics real fixture2failed; corrected scoped72passed and Roslyn6/6 compilation. | Partial original scope only; no full producer or hosted gate. |
| Current admission | Audited expanded12production/9tests/2docs estimate120000; native exec-args on installed5f8bf2da exits4, cap80000. | Complete scope deferred; no exception admission or proved under-cap decomposition. |

## Context

- Preserve existing ordinary preview support; broader three-family snapshots remain owned by workspace-fork-project-mutation-preview and its safety prerequisites, not closed here.
- Potential correct prerequisite split: shared renderer/reference preview visibility/write-path completeness, followed by dependent physical fork graph/root planning. Fresh scope, honest estimates and cold review required; never ship the incomplete restore-only fork intermediate.
- Separate assets exception-to-fresh decision is tracked by restore-assets-read-failure-false-fresh; code-fix provider silent fallback remains code-fix-provider-failure-silent-fallback.

2026-10-15 live operator reply supersedes prior execution holds: complete this initiative before new15-row delivery. Direct workflow plan: ai_docs/plans/20261015-restore-reviewed-graph.md. Preserve historical checkpoints/evidence; no completion claim until complete focused/full/hosted proof and merge.

2026-10-15 forced loader companion: current MSBuild/Roslyn roundtrip retains aliases but drops ProjectReference.EmbedInteropTypes=true (single metadata diagnostic failed expectedTrue/actualFalse). Verified upstream CreateProjectReference constructs new ProjectReference(to, aliases) without interop metadata: https://github.com/dotnet/roslyn/blob/main/src/Workspaces/MSBuild/Core/MSBuild/MSBuildProjectLoader.Worker_ResolveReferences.cs . Repair owned read authority from actual evaluated persisted items, never from the expected preview; preserve native/corrected solution pair and later text/graph apply, mismatch rejection, token/undo authority. Scope expanded for this concrete upstream integration defect; no passing complete gate claimed.

| Field | Current execution record |
|---|---|
| 2026-10-16 scope | Project-graph replay and recovery require immutable mutable-input/copy-destination inventories, required host policy approval and async recovery issuance. |
| Safety | Approve service-owned enumeration, reads, copies and writes under configured/request roots; loaded graph membership does not authorize sibling copies. Native MSBuild evaluation remains trusted execution under SECURITY.md. |
| Proof | Implementation and focused/full validation remain pending; no completion claim. |

Current-session exposed validation defect: workspace and explicit fork verdicts can report success for positive passing TRX despite failed process/envelope. Tests-first causal queued-start-causal-6d374cd5 proves nine verdict failures with nine passing controls; queued authorization moved before gates also fails all four revocation/drift controls. Repair shared verdict with timeout/zero-run precedence intact. Real fork diagnostic reaches process: MSTest net462 adapter selected from nested cache fails System.AppDomainSetup.set_ApplicationBase; original fixture runtime validity is being proved before changing fixture. No acceptance or ship claim.

Current diagnostic additionally proves both changed-workspace ValidationTool_TestRunOutlastsRequestDeadline rows fail the2-second writer completion wait after migration to native PhysicalMutationFixture. Fixture now measures real write-gate entry at the same2-second threshold, requires actual mutation success while test runner remains blocked, and preserves5-second fake deadline and final workspace-change assertions. Native completion is no longer mistaken for reader-lock acquisition. Current diagnostic suite still running; fixed fixture not yet executed.

2026-10-16 resume: cold review found test preparation skipped issued InvalidRestoreAssets and recaptured changed authored bytes as a fresh restore baseline. Require every issued native certificate before test preparation; preserve semantic degraded load and certified missing-assets restoration. Native unchanged/changed-source controls must prove refusal before any command. Source fix installed; causal and fixed verification pending.

2026-10-16 resume: shared installation metadata parser called TryGetProperty on unchecked JSON root shapes; add explicit Object guard and malformed scalar/array/object/JSON regressions for both package-library and PackageDownload paths. Guarded old-behavior verification prepared; no acceptance closure yet.

Assets-overlay receipt separation: PhysicalGraphStage previously mapped desired private project.assets.json hashes onto unchanged source paths before CaptureSourceInputsAsync, rejecting legitimate owned recovery. Keep source selecting receipts current at source; retain exact package-member proof; validate desired selectors at both private coordinates across stage replacement; publish desired selectors only in Expected. Acceptance: native owned assets-byte overlay preserves source bytes, candidate observes desired bytes, old/new receipt mismatch and current package-member grant revocation refuse. Regression: PhysicalGraphAssetsOverlayTests.OwnedAssetsOverlay_KeepsSourceAndDesiredPackageReceiptsInTheirOwnEpochs. Current source-only; parent owns causal and fixed validation.

### Current-session receipt separation and deadline repair

| Mechanism | Current evidence | Remaining gate |
|---|---|---|
| Source/desired assets epochs | assets-overlay-causal-ad4dd0418bc74f8fa585f556da487325/causal.trx: both native overlay controls fail old receipt mapping. Both controls pass in restore-focused20.trx. | Exact source selectors remain bound to source; desired selectors remain bound to private/Expected coordinates, with package-member grants checked. Full/hosted verification pending. |
| Physical identity cancellation | physical-cancellation-causal-ef8fb7100c1242d0bf33c0a58aeb9a66/causal.trx: directory/missing2 fail, file1 passes. physical-cancellation-fixed22.trx: all3 pass. | Track physical-input-identity-cancellation; keep open until gates/merge. |
| Fresh hash throughput | hash-buffer-benchmark/results.log: 64KiB versus4KiB reads retain equal complete SHA256 and cancellation controls. Allocation follow-up allocations.log proves pooled64KiB reads preserve hashes/cancellation with about1KiB allocation per capture versus67KiB buffered. PhysicalInputIdentity and held analyzer images now use StreamContentHash with fresh unbuffered streams; no byte/permission cache. PhysicalInputIdentityHashTests adds9 EOF/tail-drift lengths; StreamContentHashTests adds4 partial-read/cancellation/failure/ownership controls. | Focused22 stopped after3 recorded deadline failures and197 completed passes (200 reported; run aborted, not a completed gate). Guarded combined diagnostic7006e224ca724cb391b66ef31cd0c366 completed178pass/3fail. New pooled reader and exact editorconfig snapshot reuse await focused23; benchmark does not prove native deadlines. |
| Latest completed integrated snapshot | restore-focused20.trx:96pass/3fail/99total; restore-native-index21.trx:72pass. | Three original deadline failures remain unresolved at this snapshot. No timeout/assertion changes, full-gate approval or completion claim. |

- Evidence root: `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume`.
- Append fresh focused23 results after inspecting completion. Preserve old causal/historical entries; keep all acceptance open pending required exact-head gates and merge.

2026-10-17 integration repair: all 24 AddProjectToCopiedSolution callers were checked. Success-path fixtures now explicitly restore the edited graph using the copied SampleLib assets recorded absolute package root and matching load properties. Intentional missing-assets controls remain unchanged. Initial combined validation exposed three persistence fixture failures; focused verification of the repair is pending. Undo fixture ownership now covers restore/load failure cleanup.

## Qualification and measured serialization changes 2026-10-17

- Explicit operator approvals for the three test-contract corrections are recorded in ai_docs/reports/20261017-prerequisite-test-contract-decisions.md. Integrated172 remains red:365PASS3FAIL3platformskip; both request-only30s undo cases and production120s validation failed.
- GraphRoot causal semantic mutation changed generated AssemblyMetadata and was rejected by exact physical document-text authority. Private-reference transport omission was rejected by physical project-set authority. Both guarded sources restored exact SHA. These are rejection evidence, not a latency fix.
- Existing native profiler diagnostic181 threecasesPASS in isolation; source restored. No acceptance claim against combined172. Native evaluation counts unchanged.
- Operation-local serializer Session reuse: ReleaseABBA, fixed native two-node and real sample three-node graphs, exact serialized identity arrays checked across every iteration and variant. Sample256captures allocations555-557MB baseline to404MB candidate; elapsed761-783ms to533-575ms. No native evaluation or value reuse.
- NativeIdentityEncoding buffered exactUTF16: actualSDK724items725properties captured32times perleg, ABBA baseline101/60ms versus candidate27/32ms, same23.89MB allocation; exact item/property/ordered-file identities unchanged. All65536codeunits, surrogate and chunk boundaries and owning cancellation checks pass. No deadline or authority change.
- Installed both bounded serialization changes with regression controls; identity19267PASS/noSKIP. Expanded integrated193 now running under original24-class settings and unchanged deadlines; full gate still outstanding.

2026-10-17 superseding targeted status: the original six failures pass their approved workloads, as recorded in ai_docs/reports/20261017-test-infrastructure-simplification-audit.md and 20261017-prerequisite-test-contract-decisions.md. Latest replay-workload has seven passing graph/overlay/loader/deadline cases and one separately approved directory-identity oracle failure; that correction subsequently passes on Windows and identical-output Linux. Highest-impact infrastructure simplifications have focused evidence in the audit report. Changed-file format gate passes for 553 changed C# files with zero new findings. Full local/hosted gates remain outstanding. Historical red results above remain historical evidence, not current passing claims.
