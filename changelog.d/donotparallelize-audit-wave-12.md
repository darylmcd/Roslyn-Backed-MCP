---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `FixAllServiceIntegrationTests.cs`, `FlowAnalysisServiceTests.cs`, and `GetSyntaxTreeBudgetTests.cs` — removed all three, proven safe by repeated (3x) concurrent runs alongside parallel peers: `FixAllServiceIntegrationTests` and `GetSyntaxTreeBudgetTests` read only through the already-synchronized `WorkspaceIdCache` (no preview token minted, no apply/reload/filesystem write), and `FlowAnalysisServiceTests` loads, mutates, reloads, and closes only its own private SampleSolution copy — the same isolated shape parallel `IsolatedWorkspaceTestBase` classes already use. Each removal is documented beside its class.
