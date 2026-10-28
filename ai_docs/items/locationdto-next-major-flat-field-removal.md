# locationdto-next-major-flat-field-removal — remove deprecated flat location fields at the next major

**row:** `locationdto-next-major-flat-field-removal` · **pri:** `Defer` · **size:** `M` · **deps:** `locationdto-stage3-flat-field-deprecation`

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

## Notes

2026-10-27: cannot ship until the stage-3 deprecation is in a RELEASED minor (docs/release-policy.md:38). That minor is 4.4.0 (release-4-4-0-locationdto-deprecation-cut). Also add locationdto to the product-contract 5.0 table, which lists only updateAvailable and WorkspaceNotFound.
