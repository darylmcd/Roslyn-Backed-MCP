---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `IntegrationTests.WorkspaceCore.cs`, `MemberHierarchyCrossToolConsistencyTests.cs`, and `MetadataNameLocatorTests.cs` — removed all three opt-outs after proving each class only reads the shared sample workspace through the synchronized `WorkspaceIdCache`, with repeated concurrent runs green. Closes `donotparallelize-audit-wave-15`.
