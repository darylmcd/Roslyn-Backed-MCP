---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `WorkspaceManagerEvictionTests.cs`, `WorkspacePathMrtrWireTests.cs` and `WorkspaceReadinessReportIntegrationTests.cs`. Removed the `WorkspacePathMrtrWireTests` opt-out after repeated concurrent runs (per-test in-memory harness, class-private statics only). Retained the other two with source-adjacent comments naming the shared dependency: the process-wide `WorkspaceEvictionRegistry` recycle signal, and path-deduplicated repository-solution loads plus `Close` on the shared `WorkspaceManager`. Closes `donotparallelize-audit-wave-38`.
