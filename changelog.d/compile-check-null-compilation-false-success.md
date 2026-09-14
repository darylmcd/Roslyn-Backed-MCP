---
category: Fixed
---

- **Fixed:** Diagnostic validation now rejects missing compilation snapshots, preserves source-generator diagnostics in detail lookup and fallback caches, and orders summary groups by severity then count. Shared scope constants, deterministic Info-floor coverage, and one workspace-toolchain classifier keep validation and readiness projections consistent. Closes `compile-check-null-compilation-false-success`, `compile-check-scope-vocab-and-untested-branches`, `diagnostic-details-project-diagnostic-location-contract`, `diagnostics-summary-severity-order`, `diagnostic-info-floor-vacuous-regression`, `workspace-status-toolchain-classifier-single-owner`, `workspace-summary-redundant-filename-branch`.
