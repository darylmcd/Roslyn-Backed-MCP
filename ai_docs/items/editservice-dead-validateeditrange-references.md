# editservice-dead-validateeditrange-references — three comments name a symbol that was never created

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `tests/RoslynMcp.Tests/EditUndoCohesionTests.cs`

## Acceptance

- [ ] The `<see cref="ValidateEditRange"/>` on `EditsParamName` names the methods that actually exist (`ValidateEditShape` / `ValidateEditBounds`).
- [ ] Both `EditUndoCohesionTests` comments name `ValidateEditBounds`; a repo-wide grep for `ValidateEditRange` returns only plan/history documents.

## Evidence

Verified by grep during the PR #1241 review: `ValidateEditRange` appears in three comments but is declared nowhere — the decomposition renamed the planned helper into `ValidateEditShape`/`ValidateEditBounds` and left the old name behind. A reader grepping `ValidateEditRange` finds nothing.

The dangling `cref` compiles silently because no csproj sets `GenerateDocumentationFile`, so CS1574 never fires even under `TreatWarningsAsErrors` — see sibling row `xmldoc-crefs-not-compile-checked`.
