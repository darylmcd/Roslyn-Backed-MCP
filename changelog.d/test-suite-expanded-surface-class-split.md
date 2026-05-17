---
category: Maintenance
---

- **Maintenance:** Split `ExpandedSurfaceIntegrationTests` (683 lines, 17 methods) into three focused test classes (`ExpandedSurfaceIntegrationTests_ToolContract`, `ExpandedSurfaceIntegrationTests_RepoSolutionAnalysis`, `ExpandedSurfaceIntegrationTests_CoverageProcess`) mirroring the `IntegrationTests_*` pattern from PR #793. Class-level `[TestCategory("RepoSolution")]` and `[TestCategory("Process")]` attributes now scope the CI lane filters without per-method decoration. No product behavior changes. Closes `test-suite-expanded-surface-class-split`.
