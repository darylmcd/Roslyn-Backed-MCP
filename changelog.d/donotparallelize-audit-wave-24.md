---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `SecurityDiagnosticIntegrationTests.cs`, `SelectionRangeCodeActionTests.cs`, and `SemanticExpansionTests.cs` — removed all three after proving them safe with repeated concurrent runs (read-only or preview-only work through the synchronized shared-workspace cache, per-test isolated copies, and fixtures no other class loads or builds), with a source-adjacent comment recording each decision. Closes `donotparallelize-audit-wave-24`.
