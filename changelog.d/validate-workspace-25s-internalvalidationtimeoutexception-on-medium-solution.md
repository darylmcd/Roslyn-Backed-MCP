---
category: Fixed
---

- **Fixed:** `validate_workspace` throwing an unhandled `InternalValidationTimeoutException` after 25 seconds on large solutions (11 projects / 759 documents) instead of returning a structured failure envelope. The tool now returns `overallStatus: "timeout"` with `compileResult.cancelled: true` and a `warnings` entry naming the timed-out phase, matching the graceful-timeout behavior already present in `validate_recent_git_changes`. Closes gh #759.
