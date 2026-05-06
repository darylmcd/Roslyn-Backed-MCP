---
category: Changed
---

- **Changed:** `workspace_load` consults the on-disk `IWorkspaceCacheStore` (shipped in PR #505) before opening MSBuildWorkspace. A warm-cache hit skips the restore-race wait and validates the cached project graph + per-project metadata-reference list against the post-load snapshot; a cache miss falls through to the existing cold-load path and writes a fresh entry on success. New `cacheHit: bool` field on `_meta.gateMetrics` lets profiling isolate the warm-cache path from the cold path. Closes `workspace-load-uses-cache-fast-path`.
