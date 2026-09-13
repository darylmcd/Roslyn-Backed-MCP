# unused-code-analyzer-test-harness-wave-1 — Consolidate the first unused-code analyzer test harness pair

**row:** `unused-code-analyzer-test-harness-wave-1` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestDoubles/UnusedCodeAnalyzerTestHarness.cs` (new)
- `tests/RoslynMcp.Tests/DeadFieldDetectorTests.cs`
- `tests/RoslynMcp.Tests/DeadLocalDetectorTests.cs`

## Acceptance

- [ ] Extract the common single-document `AdhocWorkspace`, `CompilationCache`, and fail-loud `IWorkspaceManager` test helper.
- [ ] Migrate the dead-field and dead-local suites without weakening their independent regression assertions.
- [ ] Preserve explicit BCL metadata-reference selection for each caller that needs it.

## Evidence

- Adjacent review found matching local `BuildAnalyzerWithSource` and `TestWorkspaceManager` implementations in the dead-field and dead-local suites; further duplicate families require separately bounded follow-up slices.
