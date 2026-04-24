---
category: Fixed
---

- **Fixed:** `validate_recent_git_changes` now returns the structured `{category, tool, message, exceptionType, _meta}` envelope on failure instead of the bare SDK string `"An error occurred invoking 'validate_recent_git_changes'."`. `ValidationBundleTools.ValidateRecentGitChanges` wraps its dispatch in an in-body try/catch routed through `ToolErrorHandler.ClassifyAndFormat` + `InjectMetaIfPossible`; `OperationCanceledException` still propagates unwrapped so cooperative cancellation stays a protocol-level signal (`validate-recent-git-changes-bare-error-envelope`).
