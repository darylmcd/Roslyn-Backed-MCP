---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `MissingWorkspaceRootRetirementTests.cs`, `MutationAnalysisSideEffectsTests.cs`, and `NegativeEdgeCaseTests.cs`. `NegativeEdgeCaseTests` only reads through the synchronized `WorkspaceIdCache`; the other two load and mutate only private GUID-named sample copies under `TestTempRoot.Current`, with id-filtered watchers and `WorkspaceClosed` handlers. Removed all three opt-outs (each documented with a source-adjacent note) after 3x green runs alone, 3x alongside each other, and 3x in a concurrent mix with fixture-copy sibling classes (113/113 passing each time).
