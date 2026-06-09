---
category: Added
---

- **Added:** automatic workspace discovery + on-demand load for read-only tools. When a read-only, non-destructive tool is called with `workspaceId` omitted and no workspace is loaded, the server discovers the implied solution — file-anchored (walk up from a `filePath` argument to the nearest `.slnx`/`.sln`/`.csproj`) or query-anchored (a bounded scan of the client's declared roots) — and auto-loads it via `workspace_load` before retrying. A unique match loads and proceeds (`_meta.autoResolution=auto-loaded`, `_meta.autoLoadElapsedMs`); two or more candidates return a structured fast-fail listing them with a ready-to-run `workspace_load(path=…)` hint; none falls back to the existing guidance/elicitation path. Mutating, preview, and apply tools never auto-load. Closes `workspace-auto-load-on-demand`.
