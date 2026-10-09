# test-try-delete-directory-copies — TryDeleteDirectory with a bare catch is copied into 7 test files

**row:** `test-try-delete-directory-copies` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs`
- `tests/RoslynMcp.Tests/MoveTypeDiskStateTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs`

## Acceptance

- [ ] The seven private copies are gone (`rg "void TryDeleteDirectory" tests/RoslynMcp.Tests` returns nothing); each caller uses `TestFixtureFileSystem.DeleteDirectoryIfExists` or records the failure through `CleanupFailureCollector`.
- [ ] A cleanup failure is reported the way the shared helper reports it (not swallowed); the tests still pass on Windows, where a held handle is the usual cause.

## Evidence

- `rg -n "void TryDeleteDirectory" tests/RoslynMcp.Tests`: `MoveTypeDiskStateTests.cs:102` (bare `catch` at :111, "Best-effort cleanup"), `PostApplySymbolRotationTests.cs:87`, `RenameSummaryModeTests.cs:152`, `ReplaceInvocationTests.cs:658`, `SdkStyleCsprojInjectionTests.cs:164`, `WorkspaceCloseDrainTests.cs:1086`, `WorkspacePathMrtrWireTests.cs:1003`.
- Shared alternatives already present: `TestInfrastructure/TestFixtureFileSystem.cs:31` (`DeleteDirectoryIfExists`), `TestBase.cs:232`, `Helpers/CleanupFailureCollector.cs:7`.
- The probe counted 15 bare empty-catch cleanups in test helpers; these seven are the copied ones. All seven files are edited, more than the anchor sample above; the change is mechanical.
- Found by a read-only syntactic probe of `tests/RoslynMcp.Tests` at 6cf842a8 on 2026-10-09 (ast-grep plus a string/comment mask; no test executed); counts re-derived with `rg` at e52c1241 before filing.

## Context

- regression_shape: a swallowing cleanup helper copied per file beside a shared one.
