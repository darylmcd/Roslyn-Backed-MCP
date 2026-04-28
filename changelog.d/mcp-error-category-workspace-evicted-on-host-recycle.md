---
category: Fixed
---

- **Fixed:** workspace-scoped tool calls after a graceful host recycle now return `category="WorkspaceEvicted"` with `serverStartedAt` and `workspaceLoadedAt` in the structured envelope, distinguishing recycle-eviction from a typo'd `workspaceId` (was indistinguishably `category="NotFound"`). Adds `WorkspaceEvictedException` in `RoslynMcp.Core` and threads `serverStartedAt` from composition root through `WorkspaceManager`. Closes `mcp-error-category-workspace-evicted-on-host-recycle` (PR #468).
