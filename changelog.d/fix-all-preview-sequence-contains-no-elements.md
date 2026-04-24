---
category: Fixed
---

- **Fixed:** `fix_all_preview` now returns a structured `FixAllProviderCrash` envelope instead of surfacing a raw `InvalidOperationException` (notably the IDE0300 `"Sequence contains no elements"` crash) or an indistinguishable empty `FixAllPreviewDto`. `FixAllService.PreviewFixAllAsync` narrows the two provider call-site catches (`GetFixAsync` + `GetOperationsAsync`) from broad `Exception` to `InvalidOperationException` only; `FixAllPreviewDto` gains optional `Error` / `Category` / `PerOccurrenceFallbackAvailable` fields; tool description documents the envelope (`fix-all-preview-sequence-contains-no-elements`).
