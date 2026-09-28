---
category: Changed
---

- **Changed:** An unknown or never-loaded `workspaceId` still fails with `category: "NotFound"` and `exceptionType: "KeyNotFoundException"`, and the error envelope now adds an optional `reason: "WorkspaceNotFound"` field (symbol, file, and metadata-name misses carry no `reason`) plus remediation text naming `workspace_list` and `workspace_load`. When the miss races an in-call auto-reload it keeps the 4.x `category: "WorkspaceReloadedDuringCall"` and `exceptionType: "KeyNotFoundException"`, and now carries the same `reason` with workspace remediation text instead of symbol re-resolve advice. Workspace-scoped `resources/read` errors keep their category prefix and error code and add `reason: WorkspaceNotFound` to the sanitized message. **Deprecation:** 5.0 will promote both paths to `category: "WorkspaceNotFound"`; branch on `reason` now. Closes `workspace-id-unknown-error-category`.
