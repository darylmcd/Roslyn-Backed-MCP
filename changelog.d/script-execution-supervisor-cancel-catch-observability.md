---
category: Maintenance
---

- **Maintenance:** Documented the approved `ObjectDisposedException` race in `ScriptExecutionSupervisor.AbandonWorkerOnHardDeadline` — the empty catch on `timeoutCts.Cancel()` at hard deadline is now annotated explaining the expected dispose-before-cancel scenario.
