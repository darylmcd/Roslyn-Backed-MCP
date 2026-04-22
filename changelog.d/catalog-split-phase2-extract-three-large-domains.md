---
category: Changed
---

- **Changed:** `ServerSurfaceCatalog` is now a `partial` class. Workspace+server, symbol, and refactoring/code-action/undo tool entries live in `ServerSurfaceCatalog.Workspace.cs`, `ServerSurfaceCatalog.Symbols.cs`, and `ServerSurfaceCatalog.Refactoring.cs`; the remaining tools stay as `RemainingInlineTools` in `ServerSurfaceCatalog.cs`. The full `Tools` list is materialized with `Lazy<IReadOnlyList<SurfaceEntry>>` so all partial static arrays initialize before the merged list is built. Phase 2 of 5 (`server-surface-catalog-append-conflict-hotspot`).
