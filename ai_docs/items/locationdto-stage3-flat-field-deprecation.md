# locationdto-stage3-flat-field-deprecation — deprecate legacy flat location fields

**row:** `locationdto-stage3-flat-field-deprecation` · **pri:** `Defer` · **size:** `M` · **deps:** `locationdto-stage1-symbol-type-producers, locationdto-stage1-diagnostic-producers-a, locationdto-stage1-diagnostic-producers-b`

## Anchors

- `src/RoslynMcp.Core/Models/SymbolDto.cs`
- `src/RoslynMcp.Core/Models/DiagnosticDto.cs`
- `src/RoslynMcp.Core/Models/TypeUsageDto.cs`
- `docs/release-policy.md`
- `CHANGELOG.md`

## Acceptance

- [ ] Start only after one minor release containing all ADR 0001 Stage 1 producer work.
- [ ] Mark legacy flat location fields obsolete with migration text pointing to `Location`.
- [ ] Add the public migration note required by the release policy without removing any field.
- [ ] Keep next-major removal tracked by `locationdto-next-major-flat-field-removal`.

## Regression shape

A reflection test asserts all legacy fields are obsolete and `Location` remains the supported replacement.
