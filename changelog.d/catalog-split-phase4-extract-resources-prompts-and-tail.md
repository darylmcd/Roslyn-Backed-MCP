---
category: Changed
---

- **Changed:** `ServerSurfaceCatalog` partial split — Resources, Prompts, and the remaining tool tail (project-mutation, scaffolding, cross-project-refactoring, orchestration, syntax, security, scripting, configuration) now live in `ServerSurfaceCatalog.Resources.cs`, `ServerSurfaceCatalog.Prompts.cs`, and `ServerSurfaceCatalog.Orchestration.cs`. Main `ServerSurfaceCatalog.cs` is now an aggregator for `Tools` (via `s_allTools`), workflow hints, document factories, and DTOs. Phase 4 of 5 (`server-surface-catalog-append-conflict-hotspot`).
