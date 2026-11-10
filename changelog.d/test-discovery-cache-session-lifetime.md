---
category: Fixed
---

- **Fixed:** Closing or reloading a workspace now releases its cached test-discovery results, and a close that lands while discovery, diagnostics or a NuGet vulnerability-scan preparation is in flight can no longer republish a cache entry for the closed session, so repeated load/analyze/close cycles no longer accumulate entries for dead workspaces.
