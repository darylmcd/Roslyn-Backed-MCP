---
category: Fixed
---

- **Fixed:** `validate_workspace` / `validate_recent_git_changes` `overallStatus` was `compile-error` whenever `compile_check` reported `Success: false` even with `errorCount: 0` (e.g. `CompletedProjects == 0` from a zero-matching `projectFilter`). `WorkspaceValidationService.ComputeOverallStatus` now keys off `ErrorCount` and error-severity merged diagnostics instead of `!Success` alone. Closes `validate-workspace-overallstatus-false-positive`.
