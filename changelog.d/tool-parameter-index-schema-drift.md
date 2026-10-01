---
category: Fixed
---

- **Fixed:** `ToolParameterIndex` now selects parameters by the SDK-bound schema boundary instead of `[Description]`, so undescribed bound parameters reach elicitation, LSP normalization and `schemaHint`; collection-typed inputs such as `get_complexity_metrics.filePaths` are no longer skipped as services. Closes `tool-parameter-index-schema-drift`.
