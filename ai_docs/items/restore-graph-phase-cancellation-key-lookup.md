# restore-graph-phase-cancellation-key-lookup — Preserve timeout outcome when MSBuild graph cancellation leaves missing nodes

**row:** `restore-graph-phase-cancellation-key-lookup` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs`
- `tests/RoslynMcp.Tests/Workspace/ForkGraphPreparationTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreRaceTests.cs`

## Acceptance

- [ ] An expired restore inventory phase reports `TimeoutException` even when MSBuild's canceled graph work set throws a missing `ConfigurationMetadata` key while linking the incomplete graph.
- [ ] Caller cancellation retains its cancellation outcome; graph evaluation failures before the phase deadline remain evaluation errors.
- [ ] A regression fails on the unchanged pre-fix production code and passes with the fix; the affected restore tests and required full gate pass.

## Evidence

- Final merged-main `just machine-validate ci` at `16771eb0d292facb17145266259b4ebc65f57a6c` failed 1/5764: `RestoreAndReload_GateHeldPastTimeout_ThrowsTimeoutExceptionAndNeverRestores` caught `PublicInvalidOperationException` wrapping `KeyNotFoundException` for `Microsoft.Build.BackEnd.ConfigurationMetadata` during `ProjectGraph` edge linking.
- The new `RestoreGraph_PhaseExpiresBeforeGraphWorkStarts_ReportsTimeout` regression failed on unchanged pre-batch production code at `3299553a2945669a51678da05989970f756883fa` with the same exception shape. The earlier race test passed when isolated on that base, so the full gate failure is load-sensitive.
- Upstream MSBuild `ParallelWorkSet.WaitForAllWorkAndComplete` exits its work loop on cancellation and exposes completed work; `GraphBuilder.BuildGraph` subsequently indexes graph nodes and edges. See `https://source.dot.net/Microsoft.Build/Graph/ParallelWorkSet.cs.html` and `https://github.com/dotnet/msbuild/blob/main/src/Build/Graph/GraphBuilder.cs`.
