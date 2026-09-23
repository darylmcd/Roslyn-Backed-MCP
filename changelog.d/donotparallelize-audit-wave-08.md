---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `ExpandedSurfaceIntegrationTests.cs`, `ExpandedSurfaceIntegrationTests.RepoSolutionAnalysis.cs`, and `ExpandedSurfaceIntegrationTests.ToolContract.cs` — removed the opt-out on `ExpandedSurfaceIntegrationTests_ToolContract` after proving it safe with repeated concurrent runs, and documented the retained opt-outs with their concrete shared-state dependency. Closes `donotparallelize-audit-wave-08`.
