---
category: Added
---

- **Added:** `IWorkspaceCacheStore` infrastructure — bounded persistent cache for the MSBuild project graph + per-project MetadataReference list under `~/.roslyn-mcp/cache/<solution-hash>/<sdk-version>/<msbuild-graph-hash>/`. Internal service; not exposed as an MCP tool. Will be consumed by `WorkspaceManager` in a follow-on PR (`workspace-load-uses-cache-fast-path`). Closes `workspace-cache-store-infrastructure`.
