---
category: Added
---

- **Added:** `impact_analysis` now supports `summary` and `declarationsLimit` parameters, mirroring the `find_references` paging contract so large impact results no longer overflow the 120 KB MCP payload cap. Summary mode returns counts only; non-summary mode honors `declarationsLimit` with `hasMore` (`AnalysisTools`). (`impact-analysis-summary-mode`)
