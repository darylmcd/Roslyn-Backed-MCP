---
category: Fixed
---

- **Fixed:** Corrected the LRU-eviction safety claims in `WorkspaceManager` and `ToolDispatch` that overstated protection for in-flight callers. The eviction scan skips only workspaces holding their `LoadLock` (i.e. mid-`workspace_load`/reload); it never consults `WorkspaceExecutionGate`'s per-workspace reader/writer lock, so a workspace with an in-flight read or write can still be evicted and disposed mid-operation, surfacing to that caller as `WorkspaceEvictedException`. Added a characterization test pinning the gap.
