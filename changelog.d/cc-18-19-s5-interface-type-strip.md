---
category: Maintenance
---

- **Maintenance:** Reduced cyclomatic complexity in `InterfaceExtractionService.BuildUsingDirectives` and `TypeExtractionService.StripInheritanceOnlyModifiers` by extracting helper flows while preserving semantic using synthesis and extracted-member modifier cleanup behavior. Tracks `cc-18-to-19-residuals-post-top10-extraction` tranche 5. (PR #374)
