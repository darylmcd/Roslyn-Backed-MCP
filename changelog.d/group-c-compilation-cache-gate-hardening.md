---
category: Fixed
---

- **Fixed:** hardened the opt-in `ICompilationCache` liveness gate (`SymbolResolver.CanUseCompilationCache`) so it is re-evaluated on every per-project compilation fetch instead of once before the loop, and now validates that the supplied solution belongs to the workspace identified by `workspaceId` — not just its own `Workspace` back-reference — closing a cache-poisoning race a mid-scan workspace reload could otherwise trigger. `ICompilationCache` gains `IsLiveSolution`; a `compilationCache` supplied without `workspaceId` now fails loudly with `ArgumentException` instead of silently disabling caching.
