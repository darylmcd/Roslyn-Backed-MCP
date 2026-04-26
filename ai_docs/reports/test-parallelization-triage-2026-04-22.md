# Test Parallelization Triage - 2026-04-22 Plan Phase 1

<!-- purpose: Phase 1 evidence report for ci-test-parallelization-audit. -->
<!-- scope: in-repo -->

## Summary

Phase 1 did not reproduce the historical cleanup-race flakes on the current
checkout.

Local stress evidence:

- Command: `pwsh -NoProfile -ExecutionPolicy Bypass -File .\eng\verify-release.ps1 -Configuration Release -NoCoverage`
- Runs: 25 consecutive runs, 2026-04-26T18:27:36Z through 2026-04-26T20:09:03Z
- Result: 25 passed, 0 failed
- Test count per observed pass: 1001 passed, 0 failed, 0 skipped
- Ignored raw logs: `artifacts/test-parallelization-triage/20260426T182736Z/`

The PR #322 source evidence did name three historical failures, but those
failures are not currently reproducible after the intervening fixture and
workspace-load hardening already present on `main`.

## Historical Failures From PR #322

PR #322's validation note listed these failures:

| Test | Current classification | Notes |
|---|---|---|
| `PragmaScopeManipulationTests.Verify_DiagnosticFiresAtLine_IsTrue_WhenCompilerReportsIt` | historical fixture-copy / restore-race candidate, not currently reproducible | The test uses `IsolatedWorkspaceTestBase`, creates a GUID sample copy, writes `Pragma_FireSite.cs`, reloads the workspace, and asks the compiler for CS0219. It does not share the sample workspace. |
| `ProjectMutationIntegrationTests.Add_Target_Framework_When_Centralized_In_DirectoryBuildProps_Injects_Into_Csproj` | historical fixture-copy / restore-race candidate, not currently reproducible | The test creates an isolated copy, rewrites `Directory.Build.props` and `SampleLib.csproj`, loads the workspace, then mutates the project file. This is sensitive to fixture-copy and MSBuild asset stability. |
| `IntegrationTests.Preview_Token_Is_Rejected_When_Workspace_Becomes_Stale` | historical stale-workspace / isolated-copy cleanup candidate, not currently reproducible | The current equivalent is `IntegrationTests.Preview_Token_Is_Rejected_After_Two_Reloads`. It creates a sample copy manually and loads it through the shared `WorkspaceManager`. This path still deserves attention if fresh failures appear because the method deletes the copy in `finally` rather than using `IsolatedWorkspaceScope`. |

## Current Anchors

- `tests/RoslynMcp.Tests/AssemblyCleanup.cs`: assembly cleanup disposes shared
  `TestBase` resources before best-effort deletion of `%TEMP%\RoslynMcpTests`.
  No current failure stack implicates this final cleanup path.
- `tests/RoslynMcp.Tests/TestBase.cs`: `DisposeAssemblyResources` serializes on
  `_initLock`, disposes `WorkspaceManager`, clears the shared workspace id cache,
  and resets `_servicesInitialized`.
- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs`: isolated fixture cleanup
  closes the workspace before deleting the copied root.
- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs`:
  `CopyFileWithRetry` already retries sharing violations while copying sample
  fixture files after restore.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`:
  `LoadIntoSessionAsync` waits for restore artifacts to stabilize before opening
  the solution.

## Findings

1. The original "which three tests?" question is now answered by PR #322:
   `PragmaScopeManipulationTests.Verify_DiagnosticFiresAtLine_IsTrue_WhenCompilerReportsIt`,
   `ProjectMutationIntegrationTests.Add_Target_Framework_When_Centralized_In_DirectoryBuildProps_Injects_Into_Csproj`,
   and `IntegrationTests.Preview_Token_Is_Rejected_When_Workspace_Becomes_Stale`.
2. The current branch does not reproduce any of them across 25 full release
   validation runs.
3. The most likely historical root cause was not class-level parallelism itself.
   The named tests all touch isolated sample copies or workspace reload/load
   behavior, and two later hardening points now cover that surface:
   `CopyFileWithRetry` for sample-copy sharing violations and
   `WaitForStableRestoreArtifactsAsync` for restore-in-progress MSBuild assets.
4. `AssemblyLifecycle.Cleanup` and `TestBase.DisposeAssemblyResources` remain
   reasonable anchors if a future failure stack points at final assembly teardown,
   but current evidence does not justify changing them.

## Phase 2 Directive

Do not implement the old suspected teardown fix blindly.

Recommended next slice:

- If CI or a future local run produces a fresh failure, capture the exact stack
  and start with the still-risky manual isolated-copy path in
  `IntegrationTests.Preview_Token_Is_Rejected_After_Two_Reloads`: convert it to
  `IsolatedWorkspaceScope` or explicitly `WorkspaceManager.Close` the loaded
  workspace before deleting the copied root.
- If no fresh failure appears, close or reclassify `ci-test-parallelization-audit`
  as stale evidence after a small follow-up docs/state PR. A new
  `verify-release-stress.ps1` gate should be planned only if a current flake is
  observed again.

## Phase 4 Directive

Do not prune `[DoNotParallelize]` attributes from this Phase 1 evidence. The
triage did not identify any defensively opted-out class that is safe to move
back into class-level parallelism.
