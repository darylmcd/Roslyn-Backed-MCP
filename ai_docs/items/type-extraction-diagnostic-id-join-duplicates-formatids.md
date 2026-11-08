# type-extraction-diagnostic-id-join-duplicates-formatids — Hand-rolled id join duplicates FormatIds

**row:** `type-extraction-diagnostic-id-join-duplicates-formatids` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:937`
- `src/RoslynMcp.Roslyn/Helpers/InterfaceNullableProjection.cs` (`FormatIds`)
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] The distinct/ordered diagnostic id join at `TypeExtractionService.cs:937` calls the shared `FormatIds` (moved to a neutral helper if the dependency direction is wrong); behavior unchanged, existing refusal-message tests stay green.

## Evidence

- PR #2018 implementer sighting: copy-paste duplication of the same distinct/ordered id join.
