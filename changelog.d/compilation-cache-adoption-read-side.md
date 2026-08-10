---
category: Added
---

- **Added:** `SymbolResolver.ResolveByMetadataNameAsync` / `SymbolResolver.FindClosestMatchesAsync` and `SymbolHandleSerializer.FindAllByMetadataNameAsync` can now route their per-project compilation fetch through the shared `ICompilationCache`, via optional `compilationCache` + `workspaceId` parameters (every existing caller keeps the raw fetch). Routing is gated on the supplied solution being the live workspace solution, so a forked/preview solution can never be served a compilation built from the live document text — it always falls back to the raw per-project fetch. Closes the remaining `ICompilationCache` read-side adoption tail (group c: the forked-solution hazard helpers).
