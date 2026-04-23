---
category: Maintenance
---

- **Maintenance:** Reduced cyclomatic complexity in `SecurityDiagnosticService.GetAnalyzerStatusAsync` and `RestructureService.StructuralRewriter.TryMatch` by extracting helper flows while preserving analyzer-status reporting and restructure matching behavior. Tracks `cc-18-to-19-residuals-post-top10-extraction` tranche 4. (PR #371)
