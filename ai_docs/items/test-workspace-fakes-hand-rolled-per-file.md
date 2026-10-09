# test-workspace-fakes-hand-rolled-per-file — IWorkspaceManager and IWorkspaceExecutionGate are re-faked per test file

**row:** `test-workspace-fakes-hand-rolled-per-file` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/FailClosedWorkspaceManagerStub.cs`
- `tests/RoslynMcp.Tests/Helpers/PassThroughWorkspaceExecutionGate.cs`
- `tests/RoslynMcp.Tests/CodeMetricsNestingTests.cs`

## Acceptance

- [ ] A slicing pass lists the 34 + 23 fakes by behaviour (pass-through, fail-closed, single-solution, recording) and files one row per batch of about four files, dependency-ordered after the shared doubles; this row closes when those rows exist and the first batch has landed.
- [ ] Shared doubles live in `Helpers/` beside `FailClosedWorkspaceManagerStub` and `PassThroughWorkspaceExecutionGate`; a fake that needs one custom member derives from or composes the shared double instead of restating the interface.
- [ ] No behaviour change: the migrated test files pass unchanged.

## Evidence

- `rg -c "class \w+[^\n]*\bIWorkspaceManager\b" tests/RoslynMcp.Tests` → 34 declarations in 35 files; `…IWorkspaceExecutionGate\b` → 23.
- Examples: `tests/RoslynMcp.Tests/CodeMetricsNestingTests.cs:362`, `tests/RoslynMcp.Tests/DeadFieldDetectorTests.cs:482`, `tests/RoslynMcp.Tests/CompilationCacheTests.cs:211`.
- Overlap, not a duplicate: `server-probe-test-doubles-batch-1..3` move the server-probe tests onto `FailClosedWorkspaceManagerStub`; this row is the rest of the project. `test-host-service-provider-composition-dedupe` is a different duplication (host service-provider composition).
- Found by a read-only syntactic probe of `tests/RoslynMcp.Tests` at 6cf842a8 on 2026-10-09 (ast-grep plus a string/comment mask; no test executed); counts re-derived with `rg` at e52c1241 before filing.

## Context

- regression_shape: an interface fake restated in every test file that needs one. Sized as a slicing row: the full migration is far beyond one PR.
