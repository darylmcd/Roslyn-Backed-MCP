# tasks-extension-workspace-load — Task-augmented execution for workspace_load / workspace_warm

**row:** `tasks-extension-workspace-load` · **pri:** `Low` · **size:** `M` · **deps:** `tasks-extension-compatibility-decision`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceWarmTools.cs`
- `tests/RoslynMcp.Tests/TaskLifecycleWireTests.cs` (new — create -> poll -> result)

## Acceptance

- [ ] `workspace_load` and `workspace_warm` offer task-augmented execution (deferred result + `tasks/get` polling) alongside their existing progress notifications, without removing the progress path.
- [ ] Cancellation propagates through the task path, reusing the hardened gate classification (commits 656efda2 / cadc7e42 / a61c2e2a).
- [ ] One wire regression proves create -> poll -> result and one proves cancellation mid-flight.

## Evidence

`workspace_load` is the server's slowest operation and streams progress only. Anchors verified present at split time.

## Context

Split from `tasks-extension-slow-ops` (2026-09-02). Blocked until `tasks-extension-compatibility-decision` lands.

`src/RoslynMcp.Host.Stdio/Program.cs` (the `WithTasks(...)` wiring point) is shared with the two sibling execution children — chain them or co-locate the wiring in this first one and have the siblings depend on it.
