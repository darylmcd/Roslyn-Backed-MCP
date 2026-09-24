---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `PostApplySymbolRotationTests`, `PreviewApplyBoundaryRevalidationTests`, and `PreviewMultiFileEditSyntaxRegressionTests` — removed the opt-out from `PostApplySymbolRotationTests` and `PreviewMultiFileEditSyntaxRegressionTests` (each works only on its own isolated sample-solution copy through per-workspace-keyed concurrent services), proven safe by repeated concurrent runs; retained the documented opt-out on `PreviewApplyBoundaryRevalidationTests`, which mutates the process-global `SecurityOptionsSnapshot` and `RootExpansionGrantRegistry`.
