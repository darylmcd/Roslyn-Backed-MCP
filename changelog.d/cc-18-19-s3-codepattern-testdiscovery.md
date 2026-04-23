---
category: Maintenance
---

- **Maintenance:** Reduced cyclomatic complexity in `CodePatternAnalyzer.ParseSemanticQuery` and `TestDiscoveryService.FindRelatedTestsAsync` by extracting parser/test-discovery helpers while preserving existing behavior and targeted integration coverage. Tracks `cc-18-to-19-residuals-post-top10-extraction` tranche 3. (PR #366)
