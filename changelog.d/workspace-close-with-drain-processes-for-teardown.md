---
category: Fixed
---

- **Fixed:** `workspace_close` not releasing MSBuild build-server process locks on Windows after session disposal. Add `drainProcesses` parameter (default `false`) that runs `dotnet build-server shutdown` after session removal — eliminating the `Permission denied` error during `git worktree remove` in parallel-sweep teardown. Wire the flag into the `release-cut` and `reconcile-backlog-sweep-plan` skill teardown sequences.
