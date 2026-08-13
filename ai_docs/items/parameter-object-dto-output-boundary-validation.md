# parameter-object-dto-output-boundary-validation — Confine and collision-check generated DTO output

**row:** `parameter-object-dto-output-boundary-validation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Validate DTO namespace/folder segments before combining paths and require the canonical destination to remain a descendant of the project root.
- [ ] Refuse existing document, file, or type collisions before storing a preview token.
- [ ] Add table-driven rooted-path, traversal, and collision regressions proving actionable refusal, no token, and no disk mutation.

## Evidence

- DTO folder input is combined directly with the project directory; rooted or traversal segments can escape the project and persistence creates directories/writes the resulting path.
