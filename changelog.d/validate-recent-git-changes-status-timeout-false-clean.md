---
category: Fixed
---

- **Fixed:** `validate_recent_git_changes` no longer reports a `git status` collection timeout as a clean tree. When the dedicated 10-second `git status` step (distinct from the broader validation-phase timeout fixed in gh #759) exceeds its timeout, the bundle now returns `overallStatus: "git-status-unknown"` instead of silently falling back to an empty-but-trusted `changedFilePaths: []` — the timeout warning's `retryable=true` signal is now wired into the structured status callers actually branch on, not just warning text. The 10-second threshold is now configurable via `ValidationServiceOptions.GitStatusTimeout` / `ROSLYNMCP_GIT_STATUS_TIMEOUT_SECONDS` (default unchanged).
