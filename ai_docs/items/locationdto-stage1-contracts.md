# locationdto-stage1-contracts — add nested location fields to the flat DTO trio

**row:** `locationdto-stage1-contracts` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Models/SymbolDto.cs`
- `src/RoslynMcp.Core/Models/DiagnosticDto.cs`
- `src/RoslynMcp.Core/Models/TypeUsageDto.cs`
- `tests/RoslynMcp.Tests/ModelsTests.cs`

## Acceptance

- [ ] Add nullable `LocationDto? Location` fields alongside every legacy flat location quartet.
- [ ] Preserve the existing flat fields and wire names; this is ADR 0001 Stage 1's additive contract step.
- [ ] Pin camel-case serialization and null/default compatibility for all three DTOs.

## Regression shape

One model serialization test covers the three DTOs with populated and null nested locations.
