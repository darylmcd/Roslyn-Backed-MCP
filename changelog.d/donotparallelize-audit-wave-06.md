---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `DiagnosticServiceFilterTotalsTests.cs`, `DiagnosticServicePerfTests.cs`, and `DiagnosticSourceGeneratorParityTests.cs`. Removed the opt-out on `DiagnosticServiceFilterTotalsTests` — every test method only reads through the already-synchronized `DiagnosticService` cache and locally-scoped query helpers, proven safe by a repeated (3x) concurrent run alongside its wave siblings. Retained the opt-outs on `DiagnosticServicePerfTests` and `DiagnosticSourceGeneratorParityTests`, each now documented with the concrete mid-test `WorkspaceManager.ReloadAsync`/process-spawn dependency that still requires serialization.
