# lru-eviction-concurrent-reader-safety-overstated — LRU eviction can evict a workspace mid-read, contradicting documented safety claim

**row:** `lru-eviction-concurrent-reader-safety-overstated` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:329-341` (XML remarks claim LRU eviction "never yanks an in-flight workspace out from under a concurrent caller")
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:246-249` (LRU scan filters on `LoadLock.CurrentCount > 0`; comment claims this covers "actively being loaded or read")
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:818,967` (`LoadLock` is acquired/released only inside `LoadIntoSessionAsync` — the load path only)
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:51,215-225` (reads acquire a completely separate `AsyncReaderWriterLockRegistry` per workspace, never consulted by `WorkspaceManager`'s eviction scan)

## Acceptance

- [ ] Either `WorkspaceManager`'s LRU eviction scan also checks the workspace's read/write lock state from `WorkspaceExecutionGate` (or an equivalent shared signal) before selecting an eviction candidate, so a workspace mid-`compile_check`/`test_run` is never evicted — OR the two overstated safety comments (`WorkspaceManager.cs:246`, `ToolDispatch.cs:337-338`) are corrected to state the actual guarantee (in-flight *loads* are protected; in-flight *reads* are not).
- [ ] If left as a documented gap (not fixed), a regression/characterization test demonstrates the current behavior (a concurrent reader can observe `ObjectDisposedException` from LRU eviction) so the gap is pinned, not silently rediscovered later.

## Evidence

- Cold batch-level review of backlog-sweep plan `20260805T222513Z_backlog-sweep` (PR #1141, `workspace-eviction-no-auto-retry-on-tool-call`) traced `LoadLock` usage: acquired only inside `LoadIntoSessionAsync` (`WorkspaceManager.cs:818-967`), i.e. only during an active load. Ordinary reads go through `WorkspaceExecutionGate`'s separate per-workspace `AsyncReaderWriterLockRegistry` (`WorkspaceExecutionGate.cs:51`, `ReaderLockAsync`/`WriterLockAsync` at `:218,225`), which `Close()`/the eviction scan never consults. Independently re-verified by the orchestrator (grep confirms `LoadLock.WaitAsync` has exactly one call site, at the load path) before filing.

## Context

Surfaced during the Step 13 self-reflection cold review of the `workspace-eviction-no-auto-retry-on-tool-call` initiative, which made LRU eviction automatic (previously an explicit operator opt-in via `workspace_load(evictPolicy: "lru")`) — increasing exposure to this pre-existing gap. Not merge-blocking: worst case is an `ObjectDisposedException` for the unlucky concurrent caller, no data corruption. Not currently covered by the two existing eviction-retry follow-on rows (`workspace-eviction-retry-swallowed-log`, `workspace-eviction-retry-untested-branches`), which are about the retry helper's own logging/test coverage, not this LRU-scan visibility gap.
