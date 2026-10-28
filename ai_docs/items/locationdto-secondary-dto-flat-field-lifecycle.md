# locationdto-secondary-dto-flat-field-lifecycle — Give PropertyWriteDto and MutationCallerDto flat location fields a deprecation and removal row

**row:** `locationdto-secondary-dto-flat-field-lifecycle` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Models/PropertyWriteDto.cs`
- `src/RoslynMcp.Core/Models/TypeMutationDto.cs`
- `docs/decisions/0001-locationdto-nested-field-migration.md`

## Acceptance

- [ ] An explicit decision (ADR 0001 amendment or new ADR) says whether `PropertyWriteDto` and `MutationCallerDto` keep their flat coordinates permanently or follow the same additive-deprecate-remove lifecycle as `SymbolDto`, `DiagnosticDto` and `TypeUsageDto`.
- [ ] If they follow it: deprecation and removal rows exist with the same release-window rule (`docs/release-policy.md:38`).

## Evidence

- ADR 0001 stage 2 composes `LocationDto` into both DTOs additively and keeps every legacy field, but stage 3 ("all three DTOs") names only `SymbolDto`, `DiagnosticDto` and `TypeUsageDto`. Nothing tracks the secondary DTOs' flat fields, so they would stay flat-plus-nested forever by omission.
- Their flat coordinates are non-nullable, so there is no partial-span problem like the primary DTOs have; `Location` is null only when the producer's `lineSpan.Path` is empty (`SymbolMapper.ToOptionalLocationDto` requires a non-empty path), which `MutationAnalysisService` (lines 186 and 507) does not guard.
