---
category: Fixed
---

- **Fixed:** `workspace_close` no longer fails with `FileNotFoundException` when the on-disk solution file was deleted but the session is still registered — `RunWriteAsync` can skip the staleness auto-reload preflight on close via `applyStalenessPolicy` on `IWorkspaceExecutionGate`, and `CloseWorkspace` passes that so teardown does not re-open a missing path. Closes `workspace-close-missing-solution-on-disk` (PR #345).
