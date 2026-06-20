---
category: Maintenance
---

- **Maintenance:** Routed `TestReferenceMapService` and `ReferenceService` live-solution compilation reads through the shared `ICompilationCache` (3 sites) — batch a of the ongoing compilation-cache read-side adoption, for guaranteed cross-call compilation sharing under GC pressure + in-flight dedup. Covered by two new recording-cache adoption tests (`compilation-cache-adoption-read-side`, partial).
