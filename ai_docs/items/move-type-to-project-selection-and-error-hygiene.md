# move-type-to-project-selection-and-error-hygiene — Tidy move_type_to_project type selection and error construction

**row:** `move-type-to-project-selection-and-error-hygiene` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Selection is arity-aware or ambiguity is refused with each candidate named once; red-first test (a generic delegate and a class of the same name, and an enum declared before a class in one file).
- [ ] The raw InvalidOperationException throws on the move path (~:40-71) use InvalidOperationErrors/the public exception helper; the construction baseline stays unchanged or shrinks.

## Evidence

- Cold implementation review of relocation-support-enum-delegate-move-to-project (slice 2), follow-ups; worker sketches. Same family as relocation-service-type-selection-hygiene (different service).

## Notes

Slice 3 worker (#2202) sketch: TypeMoveService.PreviewMoveTypeToFileAsync has the same two raw InvalidOperationException throws (ratcheted baseline) and its name lookup duplicates CrossProjectRefactoringService.FindMovableTypeDeclaration; fold both into this row (add src/RoslynMcp.Roslyn/Services/TypeMoveService.cs as an anchor when worked).
