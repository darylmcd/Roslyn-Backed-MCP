---
category: Fixed
---

- **Fixed:** exceptions thrown by `workspace_load` or the retried tool dispatch during elicitation-based `workspaceId` recovery now return the standard structured `CallToolResult` error envelope instead of escaping the filter as unhandled transport errors.
