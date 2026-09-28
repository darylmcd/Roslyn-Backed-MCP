---
category: Changed
---

- **Changed:** An unknown or never-loaded `workspaceId` still fails with `category: "NotFound"` and `exceptionType: "KeyNotFoundException"`, and the error envelope now adds an optional `reason: "WorkspaceNotFound"` field (symbol, file, and metadata-name misses carry no `reason`) plus remediation text naming `workspace_list` and `workspace_load`; such a miss is no longer mislabeled `WorkspaceReloadedDuringCall` when it races an in-call auto-reload, and workspace-scoped `resources/read` errors keep the `NotFound:` prefix and not-found code while adding `reason: WorkspaceNotFound` to the sanitized message. **Deprecation:** 5.0 will promote this to `category: "WorkspaceNotFound"`; branch on `reason` now. Closes `workspace-id-unknown-error-category`.
