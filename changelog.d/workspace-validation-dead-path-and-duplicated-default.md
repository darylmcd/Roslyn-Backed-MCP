---
category: Maintenance
---

- **Maintenance:** Removed a dead status-degrade wrapper on `WorkspaceValidationService`'s git-status-timeout fallback branch (it could never change the timeout verdict, since `CreateTimeoutResult` always reports `"timeout"` and the degrade only fires on `"clean"`) and consolidated the duplicated 10-second git-status-timeout default onto `ValidationServiceOptions.GitStatusTimeout` as the single source of truth.
