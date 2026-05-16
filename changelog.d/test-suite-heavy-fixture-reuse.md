---
category: Maintenance
---

- **Maintenance:** Refactored `ChangeSignaturePreviewTests` to inherit `IsolatedWorkspaceTestBase`, replacing 14 repeated copy/load/close boilerplate blocks with `CreateIsolatedWorkspaceCopy()` — no behaviour change.
