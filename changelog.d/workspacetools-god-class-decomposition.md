---
category: Maintenance
---

- **Maintenance:** Split `WorkspaceTools.cs`'s readiness-report and support-bundle construction logic into dedicated `WorkspaceReadinessReportBuilder` and `WorkspaceSupportBundleBuilder` types, reducing the god-file from 1165 to 758 lines with no change to tool behavior or output shape.
