# edit-preview-validation-decomposition — Decompose edit validation and multi-file preview construction

**row:** `edit-preview-validation-decomposition` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs` (`ValidateEdits`, `BuildPatchedSourceText`, `PreviewMultiFileTextEditsAsync`, `PreviewMultiFileTextEditsOnSolutionAsync`)
- `tests/RoslynMcp.Tests/EditUndoCohesionTests.cs`
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs`

## Acceptance

- [ ] Extract coordinate validation, overlap validation, patch construction, and preview orchestration into named single-purpose helpers.
- [ ] Keep each extracted method below cyclomatic complexity 10 and 80 logical lines.
- [ ] Preserve invalid-range, overlap, cross-file ordering, and syntax-preflight behavior with focused regressions.

## Evidence

- The 2026-08-05 direct review measured `ValidateEdits` at CC 15/71 lines, `BuildPatchedSourceText` at CC 11/29, and multi-file preview methods at CC 10-11/59-70 after the selected apply-path decomposition.
