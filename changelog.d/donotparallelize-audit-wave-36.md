---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `WorkspaceLoadCacheFastPathTests.cs`, `WorkspaceCapLruEvictionTests.cs` and `WorkspaceCloseDrainTests.cs` — removed all three after proving them safe with repeated concurrent runs (per-test workspace managers, cache roots, fixture copies and fakes; helper processes reached only through the pid seam), with a source-adjacent comment recording each decision. Closes `donotparallelize-audit-wave-36`.
