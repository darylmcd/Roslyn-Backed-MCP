---
category: Changed
---

- **Changed:** `ServerSurfaceCatalogAnalyzer` (RMCP001/RMCP002) now treats `Tool()` / `Resource()` / `Prompt()` invocations that bind to `ServerSurfaceCatalog`’s private factory methods as catalog entries no matter which member’s initializer they appear in (not only the `Tools` / `Resources` / `Prompts` properties). Adds `SliceFieldDetectionTests` for slice-field and auxiliary-property scenarios. Unblocks the partial-class catalog split (`server-surface-catalog-append-conflict-hotspot` phase 1 of 5).
