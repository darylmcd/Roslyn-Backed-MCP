---
category: Fixed
---

- **Fixed:** `scaffold_test_preview` no longer emits a dotted class identifier when callers pass a fully-qualified type name after an ambiguity-resolution hint. `ScaffoldingService.PreviewScaffoldTestAsync` + `ProcessBatchScaffoldTarget` now strip input to the last identifier segment for resolver lookup, then thread the matched `INamedTypeSymbol.Name` through `BuildTestContent` to every downstream template site (filename, class-name, ctor call, static invocation, `typeof(T)`) so dotted input produces compiling single-identifier output (`scaffold-test-preview-dotted-identifier`).
