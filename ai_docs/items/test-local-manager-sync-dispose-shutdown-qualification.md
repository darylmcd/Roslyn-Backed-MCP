# test-local-manager-sync-dispose-shutdown-qualification — Qualify inner-scope synchronous shutdown before fixture deletion

**row:** `test-local-manager-sync-dispose-shutdown-qualification` · **pri:** `Medium` · **size:** `L` · **deps:** `test-local-manager-dispose-before-copy-delete`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1266` (`Dispose` requests stop), `:1274` (`DisposeAsync` awaits it)
- `tests/RoslynMcp.Tests/WorkspaceLoadAutoRestoreTests.cs:238`, `:347`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreBudgetTests.cs:181`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreRaceTests.cs:61`, `:109`, `:206`, `:264`, `:329`, `:367`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreFailureWireTests.cs:37`, `:82`
- `tests/RoslynMcp.Tests/WorkspaceLoadRestorePackagesPathTests.cs:367`, `:397`, `:428`
- `tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs:182`, `:761`
- `tests/RoslynMcp.Tests/WorkspaceCapLruEvictionTests.cs:339`

## Acceptance

- [ ] Re-derive the ownership and deletion order in the listed candidate tests. Determine whether synchronous `Dispose()` can leave shutdown pending when `finally` deletes copied roots; do not classify a candidate as a defect merely from syntax.
- [ ] Use a deterministic held-shutdown or active-session probe to show an old-code failure for each proven mechanism, then await complete shutdown before deletion while preserving primary and cleanup failures.
- [ ] If a candidate is safe, record the observed reason and retire it from this row. Keep the confirmed manager-before-try fixes in `test-local-manager-dispose-before-copy-delete` separate.

## Evidence

- 2026-11-05 cold audit: `WorkspaceManager.Dispose()` calls `RequestStop()` and does not await pending `ShutdownAsync`; `DisposeAsync()` does. Sixteen inner-scope candidates plus one explicit synchronous Dispose path need pending-shutdown qualification. No race has been reproduced yet.

## Context

- This row is an investigation of possible asynchronous teardown races. It depends on the independently correct fix for 15 definite manager-before-try ordering paths. The files span restore, analyzer, and eviction fixtures; split only after establishing a shared failure mechanism and filing dependency-chained remainder rows.
