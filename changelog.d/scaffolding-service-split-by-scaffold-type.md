---
category: Maintenance
---

- **Maintenance:** Split `ScaffoldingService.cs` (2776 lines) into three focused partial-class files by scaffold type: `ScaffoldingService.TypePreview.cs` (type scaffolding + interface resolution), `ScaffoldingService.TestPreview.cs` (single-test scaffolding + sibling-pattern inference), and `ScaffoldingService.TestBatchAndFirstTestPreview.cs` (batch-test and first-test-file scaffolding). Pure code organization — no behavior changes; all 24 ScaffoldingIntegration tests pass unchanged.
