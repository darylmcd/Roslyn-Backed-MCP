# isolated-workspace-restore-helper-deduplication — Share isolated fixture restore execution

**row:** `isolated-workspace-restore-helper-deduplication` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs`

## Acceptance

- [ ] Move isolated-solution restore execution and diagnostics into one base helper with explicit cancellation.
- [ ] Replace both private `RestoreWorkspaceAsync` copies without changing when their callers restore or load a fixture.
- [ ] Preserve bounded process execution, complete stdout/stderr failure evidence, and solution-path argument safety.
- [ ] Add one shared-helper regression that proves a restore failure retains the exit code and both output streams.

## Evidence

Dependency-validation review found functionally identical private restore helpers in the cross-project refactoring and scaffolding suites. The duplication already advertises its peer in a comment and can drift in timeout, command, or diagnostic behavior.
