---
category: Fixed
---

- **Fixed:** `workspace_close(drainProcesses=true)` now terminates detached `testhost.exe`/`vstest.console` processes scoped to the workspace directory after `dotnet build-server shutdown`, resolving `git worktree remove` failures (`Device or resource busy`) that persisted after a `test_run` on Windows. The directory filter uses a separator-terminated normalized path prefix so a sibling worktree sharing a string prefix is never killed.
