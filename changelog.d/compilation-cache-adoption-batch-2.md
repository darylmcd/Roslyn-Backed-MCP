---
category: Maintenance
---

- **Maintenance:** Route `TypeConsumersService`, `CodePatternAnalyzer`, and `SymbolSearchService` read-side compilations through the shared `ICompilationCache` instead of calling `project.GetCompilationAsync` directly (batch 2 of the `compilation-cache-adoption-read-side` adoption sweep; batch 1 shipped in PR #913). Guarantees cross-call compilation sharing under GC pressure, analyzer-bound caching, and in-flight dedup for these read-side analysis paths.
