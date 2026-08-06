# lru-eviction-gate-layer-execution — close the LRU-eviction / WorkspaceExecutionGate gap so eviction cannot dispose a workspace under an in-flight reader

**row:** `lru-eviction-gate-layer-execution` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:243-275` (LRU scan filters only on `LoadLock.CurrentCount`, never consults the gate)
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:402-421` (`Close` removes, records eviction, then disposes the `MSBuildWorkspace`)
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:64-67,213-232` (ctor takes `IWorkspaceManager`; reader lock + `EnsureWorkspaceStillExists` precede the action)
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:341-355` (auto-retry helper calls `LoadAsync` with `EvictPolicy.Lru`, making eviction automatic)
- `tests/RoslynMcp.Tests/WorkspaceCapLruEvictionTests.cs:150-222` (characterization test asserting the current unsafe behavior)
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:708-709` (`GetProject` touches `session.Workspace` after resolving the session — the second, uncharacterized failure mode)

## Acceptance

- [ ] Eviction execution moves up into the gate layer (or an equivalent shared lock-state signal is consulted) so the LRU scan cannot select and `Close()` a workspace holding a gate reader/writer lock; `LruEviction_WhileGatedReadInFlight_EvictsAnyway_AndReaderObservesEviction` is rewritten to assert the eviction blocks until the reader drains.
- [ ] Coverage extends to the second failure mode the current characterization omits — a caller that already resolved a session before `Close()` disposed it (`GetProject` touches `session.Workspace` after `GetRequiredSession` returned) — and the doc blocks at `WorkspaceManager.cs:249-266` and `ToolDispatch.cs:341-355` are updated to the new guarantee.
- [ ] Test hygiene fixed while rewriting: release the reader `TaskCompletionSource` in a `finally` and await it with `WaitAsync(ct)` so an assertion failure cannot orphan a gate-holding task.

## Evidence

- Code-quality review of PR #1159 (`lru-eviction-concurrent-reader-safety-overstated`), traced in the worktree, not hypothesized: `LoadLock.WaitAsync` has exactly one call site (`WorkspaceManager.cs:839`, inside `LoadIntoSessionAsync`), so the eviction filter excludes only mid-load sessions; the gate acquires its per-workspace reader lock and runs `EnsureWorkspaceStillExists` BEFORE invoking the action, after which nothing re-checks liveness; `Close()` removes and disposes with no gate consultation. The PR's own shipped test demonstrates the eviction firing under an in-flight gated read, and `ToolDispatch`'s auto-retry now triggers `EvictPolicy.Lru` without operator opt-in, widening exposure.

## Context

Spin-off from the `lru-eviction-concurrent-reader-safety-overstated` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1159), which resolved via the row's own documentation-correction + characterization-test acceptance branch rather than fixing the underlying gap — this row is the actual fix.
