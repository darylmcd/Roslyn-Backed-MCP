---
category: Maintenance
---

- **Maintenance:** Reduced cyclomatic complexity in `ScriptingService.EvaluateAsync` and `RefactoringService.PersistDocumentSetChangesAsync` by extracting helper flows while preserving behavior and targeted scripting coverage. Tracks `cc-18-to-19-residuals-post-top10-extraction` tranche 1. (PR #361)
