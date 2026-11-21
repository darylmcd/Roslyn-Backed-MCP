# rebase-refusal-thrown-vs-dto-consistency — One refusal shape for rebase conflicts: the thrown PublicInvalidOperationException at RefactoringService ~:1132 versus DTO refusals

**row:** `rebase-refusal-thrown-vs-dto-consistency` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`

## Acceptance

- [ ] All rebase conflict refusals use one shape; evidence that the change is a major (error to success-shaped refusal), ADR plus migration note.

## Evidence

- Plan of rebase-changed-document-silent-drop.
