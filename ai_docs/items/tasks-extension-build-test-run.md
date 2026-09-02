# tasks-extension-build-test-run — Task-augmented execution for build_* and test_run

**row:** `tasks-extension-build-test-run` · **pri:** `Low` · **size:** `M` · **deps:** `tasks-extension-workspace-load`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs`
- `tests/RoslynMcp.Tests/TaskLifecycleBuildTestWireTests.cs` (new)

## Acceptance

- [ ] `build_project`, `build_workspace` and `test_run` offer task-augmented execution alongside their existing progress notifications.
- [ ] Cancellation propagates through the task path for long test runs specifically, where the existing timeout budget interacts with the gate.
- [ ] One wire regression per family proves create -> poll -> result.

## Evidence

These are the remaining long-running operations named by the parent row's acceptance bullet 1 alongside `workspace_load`.

## Context

Split from `tasks-extension-slow-ops` (2026-09-02). Depends on `tasks-extension-workspace-load` so the `WithTasks(...)` wiring in `src/RoslynMcp.Host.Stdio/Program.cs` lands once and this child only adds tool-side opt-in.
