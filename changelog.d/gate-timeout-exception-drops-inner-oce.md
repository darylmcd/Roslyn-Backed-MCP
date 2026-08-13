---
category: Fixed
---

- **Fixed:** `TimeoutException` raised by the gate's internal-timeout reclassification (`WorkspaceExecutionGate.RunPerWorkspaceAsync`/`RunLoadGateAsync`, `GatedCommandExecutor.ExecuteAsync`) now carries the original `OperationCanceledException` as `InnerException`, preserving cancellation provenance (which token fired, original stack trace) in logs and diagnostics instead of discarding it.
