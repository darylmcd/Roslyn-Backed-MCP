---
category: Changed
---

- **Changed:** Workspace load evaluates each project once for one set of global properties. `BuildLoadProjection` reuses a single MSBuild project collection for target framework, output type, and restore-assets reads, then unloads and disposes that collection when the projection returns, including when it fails. Reads outside that projection still use their own collection.
