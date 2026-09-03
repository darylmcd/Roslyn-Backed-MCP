---
category: Changed
---

- **Changed:** The `_meta` gate-metrics block attached to every tool response now omits unset observability fields (`staleAction`, `staleReloadMs`, `retriedAfterReload`, `cacheHit`, `reloadConfirmedNotFound`, `autoResolution`, `autoLoadElapsedMs`, `heartbeatCount`, `gateMode`) instead of serializing them as explicit `null`, trimming response bytes on the common case (#1421).
