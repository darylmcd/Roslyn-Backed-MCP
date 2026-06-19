---
category: Maintenance
---

- **Maintenance:** Stripped the remaining internal `BUG-008` tracker id from two MSBuild eval-service comments (`MsBuildEvaluationService.cs`, `IMsBuildEvaluationService.cs`), preserving the filter-to-avoid-large-output rationale. Completes the internal-comment surface left by the `legacy-bug-id-tool-descriptions` (#966) cleanup. Closes `legacy-bug-id-msbuild-eval-comments`.
