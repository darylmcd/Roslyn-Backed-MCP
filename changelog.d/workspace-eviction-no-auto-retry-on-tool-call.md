---
category: Fixed
---

- **Fixed:** `compile_check` and `test_run` now transparently reload an evicted workspace (via its recorded `LoadedPath`) and retry once instead of surfacing a hard failure — covers both the direct `WorkspaceEvicted` classification and the more common case where the workspace was evicted before the call started.
