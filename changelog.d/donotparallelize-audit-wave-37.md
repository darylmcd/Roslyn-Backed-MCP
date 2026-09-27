---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `WorkspaceEvictionAutoRetryTests.cs`, `WorkspaceLoadDedupTests.cs` and `WorkspaceLoadRestoreRaceTests.cs` — removed the first two after repeated concurrent runs proved them safe (each works only against its own `WorkspaceManager` or its own isolated sample-solution copy), and retained the third with a source-adjacent comment naming its wall-clock load-time ceilings, which a concurrent run breached (12244 ms against a 10000 ms ceiling). Closes `donotparallelize-audit-wave-37`.
