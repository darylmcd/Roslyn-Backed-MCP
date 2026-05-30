---
category: Maintenance
---

- **Maintenance:** Extracted the duplicated `FormatTypeName` (Nullable-unwrap + generic-arity strip + primitive-to-C#-keyword map) into a shared `RoslynMcp.Host.Stdio.Catalog.CatalogTypeNameFormatter`. The 29-LOC method was byte-identical in `PromptParameterIndex` and `ToolParameterIndex` (same `Catalog/` folder); both now delegate to the helper. Behavior is unchanged. Closes `catalog-formattypename-dedup`.
