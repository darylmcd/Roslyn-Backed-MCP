---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `PerformanceBaselineTests`, `PerformanceBehaviorTests`, and `PositionProbeTests` — removed the opt-out from `PositionProbeTests` (read-only through the synchronized `WorkspaceIdCache`) and `PerformanceBehaviorTests` (private temp-copy workspace, now closed after use, plus local fakes), proven safe by repeated concurrent runs; retained it on `PerformanceBaselineTests`, documenting that its wall-clock budgets depend on not sharing the process CPU with sibling classes.
