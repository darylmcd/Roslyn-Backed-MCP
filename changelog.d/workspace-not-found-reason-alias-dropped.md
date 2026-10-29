---
category: Changed — BREAKING
---
- **Changed — BREAKING:** The `reason` field is removed from error envelopes: the `reason: "WorkspaceNotFound"` that 4.x added to unknown-`workspaceId` errors is gone (it was the only value), and `resources/read` failures no longer append `; reason: WorkspaceNotFound` to the message. The `WorkspaceNotFound` category and `WorkspaceNotFoundException` exception type identify the miss. Migration: branch on `category: "WorkspaceNotFound"` and `exceptionType` instead of `reason`, and stop reading `reason` from error envelopes.
