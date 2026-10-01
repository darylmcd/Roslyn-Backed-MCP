---
category: Changed
---

- **Changed:** Invalid `ROSLYNMCP_ON_STALE`, `ROSLYNMCP_OBSERVABILITY_SINK`, and tool-tier env values now fail startup with `InvalidOperationException` instead of `ArgumentException`; the `workspace_close` drain-timeout guard is an internal invariant, not a caller error.
