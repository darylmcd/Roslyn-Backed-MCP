---
category: Fixed
---

- **Fixed:** residual-case elicitation recovery (`IsWorkspaceIdRecoveryAllowedFor`) now fires for tools with `workspaceId` flipped to `Required:false` (e.g. `go_to_definition`, `find_references`, `document_symbols`), matching the design intent documented on `IsWorkspaceIdAutoResolveAllowedFor`; stale comment in `TryAutoLoadWorkspaceAsync` corrected.
