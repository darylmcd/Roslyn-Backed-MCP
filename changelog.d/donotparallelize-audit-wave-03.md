---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `BacklogFixTests`, `BuildTestToolsShimTests`, and `BulkRefactoringTests`. Removed the opt-out from `BacklogFixTests` and `BuildTestToolsShimTests` after confirming every test method only reads through the already-synchronized `WorkspaceIdCache`/`WorkspaceExecutionGate` read path and validating with a >=3x repeated run alongside its wave siblings. Retained `[DoNotParallelize]` on `BulkRefactoringTests` and documented the concrete shared-state dependency: it calls `WorkspaceManager.LoadAsync`/`.Close` directly on the assembly-shared `WorkspaceManager` instance.
