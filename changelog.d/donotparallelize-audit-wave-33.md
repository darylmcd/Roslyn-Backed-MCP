---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `UnusedSymbolsTestBridgeExclusionTests.cs`, `ValidateRecentGitChangesTests.cs` and `ValidateWorkspaceChangeTrackerReconcileTests.cs`. Removed the opt-outs on `UnusedSymbolsTestBridgeExclusionTests` (read-only query against the synchronized shared-workspace cache) and `ValidateWorkspaceChangeTrackerReconcileTests` (per-test isolated workspace copies) after repeated concurrent runs stayed green. Retained the opt-out on `ValidateRecentGitChangesTests` and documented why: it mutates the process-global `GIT_DIR`/`GIT_WORK_TREE` variables that concurrent git fixture processes inherit. Closes `donotparallelize-audit-wave-33`.
