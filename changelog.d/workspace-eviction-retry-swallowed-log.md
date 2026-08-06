---
category: Fixed
---

- **Fixed:** the workspace-eviction auto-retry seam (`compile_check`/`test_run`) now logs the swallowed reload failure (deleted `LoadedPath`, cap-saturated reload) via an optional DI-bound `ILoggerFactory`, matching the `workspace_load` logging pattern — a failed auto-recovery is no longer silent. Fallback behavior (rethrow of the original error) is unchanged.
