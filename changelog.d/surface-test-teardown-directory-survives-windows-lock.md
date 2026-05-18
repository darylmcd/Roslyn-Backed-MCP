---
category: Fixed
---

- **Fixed:** `mcp-server-surface-test` Phase 6z teardown leaving the disposable worktree directory undeletable on Windows 11. The teardown sequence now calls `workspace_close(drainProcesses=true)` before `git worktree remove --force`, releasing the MCP host's analyzer DLL lock that `dotnet build-server shutdown` alone does not cover. Closes gh #745.
