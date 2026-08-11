---
category: Fixed
---

- **Fixed:** `CompilationCache.GetCompilationWithAnalyzersAsync` now short-circuits immediately for a caller whose token is already canceled on a cache miss, instead of first starting the analyzer-bound build (and its nested uncancelable `GetCompilationAsync` pass) and installing an `_analyzerBound` entry for a result the caller could never observe — mirroring the guard `GetCompilationAsync` already had. Added regression coverage proving both effects: the caller observes its own cancellation, and no `_analyzerBound` entry is installed. The `ICompilationCache` contract now states a single symmetric cancellation guarantee for both methods instead of documenting the asymmetry as deliberately out of scope. (compilation-cache-analyzers-entry-guard)
