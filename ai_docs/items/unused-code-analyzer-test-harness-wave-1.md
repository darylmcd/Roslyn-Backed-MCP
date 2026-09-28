# unused-code-analyzer-test-harness-wave-1 — Analyzer-family tests each keep a private fail-loud workspace-manager copy instead of the shared fail-closed stub (wave 1 of 3)

**row:** `unused-code-analyzer-test-harness-wave-1` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/FailClosedWorkspaceManagerStub.cs:12-66`
- `tests/RoslynMcp.Tests/Helpers/UnusedCodeAnalyzerTestHarness.cs` (new)
- `tests/RoslynMcp.Tests/DeadFieldDetectorTests.cs:443-522`

## Acceptance

- [ ] `FailClosedWorkspaceManagerStub` gains init-only `GetCurrentVersionHandler`, `ContainsWorkspaceHandler` and `IsStaleHandler`, and each member still throws while its handler is unset. These are the members the seven analyzer-family suites of waves 1-3 call beyond `GetCurrentSolution` (Evidence), so waves 2 and 3 need no further stub edits.
- [ ] A new `tests/RoslynMcp.Tests/Helpers/UnusedCodeAnalyzerTestHarness.cs` builds the single-document `AdhocWorkspace`, the `CompilationCache` and the `UnusedCodeAnalyzer` over that stub. Each caller passes its own metadata references.
- [ ] `DeadFieldDetectorTests` deletes its private `BuildAnalyzerWithSource` body and `TestWorkspaceManager` and uses the harness. Its assertions are unchanged, and it keeps its test count and passes.
- [ ] Placement: the harness goes in `tests/RoslynMcp.Tests/Helpers/` beside `FailClosedWorkspaceManagerStub`, where `server-probe-test-doubles-batch-1` also puts its shared version provider. No `TestDoubles/` folder is created, and no existing shared double is moved (`TestInfrastructure/` keeps its own).
- [ ] This row adds no new fail-loud `IWorkspaceManager` double: the harness uses `FailClosedWorkspaceManagerStub`, and the retired `DeadFieldDetectorTests.TestWorkspaceManager` is not replaced by another copy. The uncalled `RaiseWorkspaceClosed` / `RaiseWorkspaceReloaded` helpers are not carried over.

## Evidence

- The 2026-09-12 adjacent review found matching local `BuildAnalyzerWithSource` and `TestWorkspaceManager` implementations in the dead-field and dead-local suites.
- Re-derived at `716b5e20` by the PR #1665 review: seven analyzer-family suites keep a private copy of the same fake. Five are byte-identical 41-line copies: `DeadFieldDetectorTests.cs:482`, `DeadLocalDetectorTests.cs:659`, `DuplicateHelperDetectionTests.cs:571`, `DuplicateMethodDetectorTests.cs:1013` and `UnusedSymbolScanFailSafeTests.cs:267`. Two are near-copies: `CodeMetricsNestingTests.cs:362` adds an `onGetCurrentSolution` callback, and `CompilationCacheTests.cs:211` (`FakeWorkspaceManager`) swaps the workspace for a settable version.
- The subjects call only `GetCurrentSolution` and `GetCurrentVersion` (`UnusedCodeAnalyzer`, `CompilationCache`, `DeadCodeService`, `DuplicateMethodDetectorService`, `CodeMetricsService`), plus event subscription, which the stub already accepts. `DuplicateMethodDetectorTests.cs:900` also runs a real `WorkspaceExecutionGate`, which calls `ContainsWorkspace` and `IsStale`. No test calls the fakes' `RaiseWorkspace*` helpers.
- The stub already takes `GetStatusHandler` and `GetCurrentSolutionHandler` (`Helpers/FailClosedWorkspaceManagerStub.cs:16-17`). The full recount of private workspace-manager fakes is in `server-probe-test-doubles-batch-1`.

## Context

- Wave 1 of 3. `unused-code-analyzer-test-harness-wave-2` (dead-local, duplicate-helper, unused-symbol fail-safe) and `unused-code-analyzer-test-harness-wave-3` (duplicate-method, code-metrics, compilation-cache) depend on this row.
- Re-scoped by the PR #1665 review. The original row migrated the dead-field/dead-local pair onto a new double in `TestDoubles/`. That would have been a second fail-loud double beside `FailClosedWorkspaceManagerStub`, so this row now extends the stub instead. Dead-local moved to wave 2 to keep this row at 3 test files.
- `unused-code-analyzer-dead-field-complexity` also anchors `DeadFieldDetectorTests.cs`. Sequence by hotspot if both are picked up.
