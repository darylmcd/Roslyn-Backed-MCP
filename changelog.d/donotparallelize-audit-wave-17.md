---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `NuGetDependencySummaryTests.cs`, `NuGetVulnerabilityScanIntegrationTests.cs`, and `P4BehavioralBundleTests.cs` — removed the class-level opt-outs proven safe by repeated concurrent runs, and narrowed the vulnerability-scan class's opt-out to its live `Network` test, documented with its concrete shared-state dependency (an implicit restore against the shared sample fixture). Closes `donotparallelize-audit-wave-17`.
