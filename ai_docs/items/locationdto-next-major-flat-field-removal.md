# locationdto-next-major-flat-field-removal — remove deprecated flat location fields at the next major

**row:** `locationdto-next-major-flat-field-removal` · **pri:** `Defer` · **size:** `L` · **deps:** `locationdto-stage3-flat-field-deprecation`

## Anchors

- `docs/decisions/0001-locationdto-nested-field-migration.md`
- `src/RoslynMcp.Core/Models/SymbolDto.cs`
- `src/RoslynMcp.Core/Models/DiagnosticDto.cs`
- `src/RoslynMcp.Core/Models/TypeUsageDto.cs`
- `CHANGELOG.md`

## Acceptance

- [ ] Start only for the next planned major after the Stage 3 deprecation window completes.
- [ ] Split consumers into bounded implementation rows before changing production code.
- [ ] Record the breaking-change migration in the changelog and release notes.
- [ ] Remove the legacy flat fields only after compatibility tests prove the supported nested shape.

## Regression shape

Wire-schema tests prove the nested location contract and the intentional absence of legacy fields.
