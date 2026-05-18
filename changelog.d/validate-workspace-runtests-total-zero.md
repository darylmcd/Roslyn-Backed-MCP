---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Fixed `validate_workspace(runTests=true)` falsely reporting `overallStatus=clean` when `testRunResult.total=0` despite a non-empty discovered test filter. Status now returns `test-zero-run` and the response includes a diagnostic warning identifying the likely filter-resolution failure (working-directory or `IChangeTracker` timing). Breaking: callers exact-matching `overallStatus="clean"` need to handle `"test-zero-run"` as a non-passing verdict. (Fixes gh #764)
