---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `Top10V2RegressionTests.cs`, `Top10V3RegressionTests.cs` and `TypeConsumersServiceTests.cs` — removed all three after proving them safe with repeated concurrent runs (read-only queries against the synchronized shared-workspace cache; the only `*_apply` tests run against per-test isolated workspace copies), with a source-adjacent comment recording each decision. Closes `donotparallelize-audit-wave-31`.
