---
category: Maintenance
---

- **Maintenance:** The `ServerSurfaceCatalog` partial-class split and analyzer relaxation from PRs #330–#338 are complete. New and extended tools are filed in domain partials, so merge contention on a single append-only catalog is resolved; optional DTO relocation to `SurfaceTypes.cs` was skipped after post-Phase-4 review — `ServerSurfaceCatalog.cs` is no longer a repeat-conflict driver for unrelated initiatives. Closes `server-surface-catalog-append-conflict-hotspot`.
