# cross-project-move-receiver-type-collision — Refuse destination type identity collisions

**row:** `cross-project-move-receiver-type-collision` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:85`
- `tests/RoslynMcp.Tests/CrossProjectPartialTypeMoveIntegrationTests.cs`

## Acceptance

- [ ] Through experimental move_type_to_project_preview, reproduce disjoint source and target types with one partial declaration each. Different filenames and the same destination metadata identity must refuse before a new token, even when the resulting project would compile after merging the declarations.
- [ ] Use the actual target compilation, namespace, and arity. Preserve valid single-declaration moves and leave workspace state and files unchanged on refusal.

## Evidence

- At 9e7a9439 the move checks the output filename against disk before adding the declaration, but checks no same-symbol target identity in a differently named file. Separate partial types can merge into one compile-valid receiver type.
- This is a destination identity defect, separate from source declarations being split across projects.
