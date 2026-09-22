---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `AnalyzerShadowLoaderLifecycleTests.cs`, `AuditFixesTests.cs`, and `AutoReloadCascadeHostCrashTests.cs` — removed the opt-out proven safe by repeated concurrent runs, and documented the retained ones with their concrete shared-state dependency.
