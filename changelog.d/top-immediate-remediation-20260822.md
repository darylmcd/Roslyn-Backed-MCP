---
category: Fixed
---

- **Fixed:** `test_run` now preserves partial TRX counts while attaching a failure envelope whenever the underlying process exits nonzero.
- **Fixed:** Workspaces loaded beneath `.worktrees/<id>` now observe their own external edits while primary checkouts still ignore nested worktree noise.
- **Fixed:** Workspace lifecycle subscriber failures and file-watcher root-retirement subscriber failures now log only event, workspace, and exception-type metadata.
- **Fixed:** The release-managed-file hook now fails closed on malformed non-empty input without echoing the rejected payload.
- **Fixed:** Metadata-name disambiguation uses the shared compilation cache in production, cancellation follows the documented `OperationCanceledException` contract, and document lookup uses platform-aware filesystem identity.
