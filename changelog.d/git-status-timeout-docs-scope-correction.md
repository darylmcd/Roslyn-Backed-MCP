---
category: Fixed
---

- **Fixed:** `GitStatusTimeout`/`ROSLYNMCP_GIT_STATUS_TIMEOUT_SECONDS` docs now correctly state the timeout bounds the `git status` subprocess in both `validate_recent_git_changes`'s scope collection and `validate_workspace`'s change-tracker reconcile, and that only the former degrades the verdict to `git-status-unknown`.
