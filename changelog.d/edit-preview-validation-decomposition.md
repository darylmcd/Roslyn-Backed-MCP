---
category: Maintenance
---

- **Maintenance:** decomposed `EditService.ValidateEdits`, `BuildPatchedSourceText`, and the duplicated `PreviewMultiFileTextEditsAsync`/`PreviewMultiFileTextEditsOnSolutionAsync` preview-orchestration bodies into named single-purpose helpers, reducing per-method cyclomatic complexity below 10 with no change to coordinate-validation, overlap-validation, or syntax-preflight behavior.
