---
category: Fixed
---

- **Fixed:** `GatedCommandExecutor` now prunes its per-workspace command-execution semaphore on `workspace_close` (including LRU eviction), preventing unbounded growth of its internal gate dictionary across repeated load/close cycles. `PersistentCompositeStorage.TryRead` no longer throws uncaught when a preview-token subdirectory is deleted concurrently by another process; the cross-process race is now treated as a cache miss.
