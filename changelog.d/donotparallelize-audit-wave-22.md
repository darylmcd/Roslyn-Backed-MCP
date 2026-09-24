---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `RefactoringToolsIntegrationTests.cs`, `ReferenceServiceFindImplementationsTests.cs`, and `RenameSummaryModeTests.cs` — removed all three after repeated concurrent runs proved them safe (shared-workspace access is preview/read-only through the synchronized `WorkspaceIdCache`; apply/reload paths use GUID-unique workspace copies whose undo/change state is keyed by workspace id), each with a source-adjacent comment recording the evidence. Closes `donotparallelize-audit-wave-22`.
