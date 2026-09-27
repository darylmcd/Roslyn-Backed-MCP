---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `TypeExtractionTests.cs`, `TypeMoveTests.cs` and `UnresolvedAnalyzerReferenceStripperTests.cs` — removed all three after proving them safe with repeated concurrent runs (each test works on its own GUID-unique fixture copy, with no shared-workspace reload, static or environment mutation), with a source-adjacent comment recording each decision. Closes `donotparallelize-audit-wave-32`.
