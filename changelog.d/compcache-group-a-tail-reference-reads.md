---
category: Maintenance
---

- **Maintenance:** Routed the live-solution `GetCompilationAsync` reads in `ImpactSweepService`, `MutationAnalysisService`, and `SymbolRelationshipService` through `ICompilationCache` (compilation-cache read-side adoption, group-a tail — mirrors the batch-a core pass). Forked-solution call sites remain on direct `GetCompilationAsync` by design. Partial adoption: the group-b/c remainder of the parent row is still open.
