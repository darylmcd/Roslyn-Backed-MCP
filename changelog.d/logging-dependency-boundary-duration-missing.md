---
category: Added
---

- **Added:** Structured logs now carry dependency-boundary records that join the tool record by `correlationId`: `CommandProcessCompleted` (`verb`, `targetKind`, `outcome`, `exitCode`, `durationMs`) for every `dotnet` command, including cancelled and failed-to-start ones, `CommandGateCompleted` (adds `queueWaitMs`, and reports `timeout` and `cancelled` outcomes) for gated commands, and `WorkspaceLoadCompleted` (`outcome`, `reused`, `projectCount`, `durationMs`) for `workspace_load`. The records never carry command arguments, filter text or paths.
