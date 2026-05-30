---
category: Maintenance
---

- **Maintenance:** Read-side analysis services `CouplingAnalysisService`, `ExceptionFlowService`, and `AnalyzerInfoService` now obtain project compilations through the shared version-keyed `ICompilationCache` instead of calling `project.GetCompilationAsync` directly, so they share warm compilations (plus analyzer-bound caching and in-flight dedup) with the rest of the read-side tools. First bounded batch of `compilation-cache-adoption-read-side` (3 of the ~24 call sites); the row is re-scoped to the remaining follow-on sites.
