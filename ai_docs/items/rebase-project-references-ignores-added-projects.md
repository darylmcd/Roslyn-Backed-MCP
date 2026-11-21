# rebase-project-references-ignores-added-projects — RebaseProjectReferences does not replay added projects

**row:** `rebase-project-references-ignores-added-projects` · **pri:** `Low` · **size:** `S` · **verified:** `6f04249ec1c1681814a65e21bbbcc07a9dde2b61`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`
- `tests/RoslynMcp.Tests/RebaseDroppedIntentRefusalTests.cs`

## Acceptance

- [ ] A preview adding a project and a reference to it applies correctly after the workspace moved (red-first test), or the refusal is the documented, tested outcome with the reason in the row.

## Evidence

- Cold implementation review of rebase-changed-document-silent-drop (#2190), follow-up 2.
