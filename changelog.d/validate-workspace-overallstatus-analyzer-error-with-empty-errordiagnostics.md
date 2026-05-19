---
category: Fixed
---

- **Fixed:** `validate_workspace` / `validate_recent_git_changes` returning `overallStatus=analyzer-error` with empty `errorDiagnostics` and no count when `summary=true`. A new `errorCount` field on the response DTO is now always populated (mirrors the existing `warningCount` pattern) so callers in summary mode can see how many errors drove the verdict without needing the full per-item list. Closes gh #751.
