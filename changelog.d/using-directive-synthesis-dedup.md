---
category: Maintenance
---

- **Maintenance:** Extracted the duplicated using-directive synthesis cluster into a shared `RoslynMcp.Roslyn.Helpers.UsingDirectiveSynthesizer`. `BuildUsingDirectives` and its six helpers (`PreserveSpecialAndRequiredSourceUsings`, `AddMissingRequiredUsingDirectives`, `SortUsingDirectives`, `GetUsingNamespace`, `IsSystemUsingDirective`, `IsSpecialUsingDirective`) were byte-identical in `InterfaceExtractionService` and `CrossProjectRefactoringService` — the source doc-comment literally said "Mirrors `InterfaceExtractionService.BuildUsingDirectives`" — and the two copies could drift independently. Both services now delegate to the shared helper; behavior is unchanged. Closes `using-directive-synthesis-dedup`.
