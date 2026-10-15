# restore-callers-missing-packages-path — Preserve recorded roots and the reviewed fork graph

**row:** `restore-callers-missing-packages-path` · **pri:** `Medium` · **size:** `L` · **deps:** `workspace-restore-packages-path`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:393-421`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:374`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:128-149`
- (new) `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:644-731`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:375-485`
- (new) `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs`
- (new) `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:830-868`
- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs:67-115`
- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:211-252`
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

- [ ] One shared required restore plan preserves each relevant project's recorded nullable package root with platform identity; uniform roots restore the target once, mixed roots restore every dependency project with --no-dependencies and one total timeout/cancellation budget.
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
