---
category: Maintenance
---

- **Maintenance:** Extracted cobertura XML aggregation, test-project partitioning, and failure-envelope construction from `TestCoverageTools.RunTestCoverageCore` into a new `TestCoverageCoordinator` service under `RoslynMcp.Roslyn.Services`. The tool method is now a thin orchestrator over the coordinator + a private `RunCoveragePassAsync` helper for the classic/partial dotnet-test branches. Closes `test-coverage-tools-runtestcoveragecore-split` from the 2026-05-26 discovery-sweep refactor audit.
