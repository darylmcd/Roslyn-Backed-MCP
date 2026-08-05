# workspace-eviction-retry-swallowed-log — log the swallowed workspace-rehydration failure

**row:** `workspace-eviction-retry-swallowed-log` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:372` (`catch (Exception) { return null; }` around the reload)
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:345` (XML remark claiming "no diagnostic is lost")
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:16` (class-level dispatch-shape list)
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:42` (existing optional DI-bound `ILogger?` precedent)

## Acceptance

- [ ] `TryReloadEvictedWorkspaceForRetryAsync` logs the swallowed reload exception (optional DI-bound `ILoggerFactory?`, matching the `WorkspaceTools` pattern) before falling back to the original error; fallback behavior itself is unchanged.
- [ ] The XML remark no longer claims "no diagnostic is lost" for the gate-precheck path — on that path the caller rethrows the plain `KeyNotFoundException`, which carries none of the eviction context.
- [ ] The class-level dispatch-shape list in `ToolDispatch.cs`'s remarks includes the eviction-retry shape (currently enumerates only 3, this is a 4th).

## Evidence

- Code-quality review of PR #1141 (`workspace-eviction-no-auto-retry-on-tool-call`): `WorkspaceManager.LoadAsync` logs only when `session.WorkspaceDiagnostics.Length > 0` (`WorkspaceManager.cs:334-345`), while the two most likely rehydration failures — a deleted solution path, and the cap `InvalidOperationException` "already tracking N workspaces" (`WorkspaceManager.cs:285`) — both throw before any session diagnostics exist and emit no log. The `catch (Exception)` then returns null and the caller rethrows the gate's bare NotFound, so a failed auto-recovery is completely invisible to the operator.

## Context

Spin-off from the `workspace-eviction-no-auto-retry-on-tool-call` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1141). Not a correctness regression — the retry degrades to exactly pre-fix behavior on every failure path — but the failure is silent where it doesn't need to be.
