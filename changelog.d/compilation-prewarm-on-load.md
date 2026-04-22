---
category: Added
---

- **Added:** `workspace_warm(workspaceId, projects?)` MCP tool — opt-in prewarm that forces Roslyn `GetCompilationAsync` + semantic-model resolution across the workspace (or a filtered project set), cutting the first-`symbol_search` latency from ~4600 ms to <100 ms for callers that invoke it after `workspace_load`. Never runs automatically (per the 2026-04-14 user preference against blocking tool registration); caller receives `projectsWarmed`, `elapsedMs`, and `coldCompilationCount` so budget decisions stay caller-side. Three-layer shape: `IWorkspaceWarmService` / `WorkspaceWarmResult` (Core), `WorkspaceWarmService` (Roslyn, read-lock scoped), `WorkspaceWarmTools` (Host.Stdio) + `ServerSurfaceCatalog` entry + DI registration (`compilation-prewarm-on-load`).
