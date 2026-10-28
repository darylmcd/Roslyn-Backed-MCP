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

## Notes

2026-10-27: ships in 4.4.0 from release/4.x (tag v4.3.0), not from main, because 9 BREAKING fragments on main force any cut from main to a major (-RequireConsumedFragments). Precondition met: v4.3.0 contains nested Location on SymbolDto/DiagnosticDto/TypeUsageDto. Internal consumers of the flat fields must migrate (CS0618, no suppression). Tracked by release-4-4-0-locationdto-deprecation-cut; forward-port to main closes this row.

2026-10-27 operator decision: stage 3 may use a scoped #pragma warning disable CS0618 limited to the flat-field compatibility test classes (tests that assert the legacy flat contract, e.g. LocationDtoMigrationTests), each cited to this row; no other suppression anywhere. Everything else migrates to Location. Sizing evidence: probe of [Obsolete] on the 18 flat params at v4.3.0 gave 128 unique CS0618 sites in 26 files (tests 74, RoslynMcp.Roslyn 39, Host.Stdio 15); production side is about 54 sites in 12 files.

2026-10-28 operator decision: option A. Production reads and writes of the flat members go through one internal adapter (src/RoslynMcp.Core/Models/LocationDtoFlatAccess.cs) holding the only production CS0618 pragma; tests read through the same seam, and flat-versus-Location agreement assertions live in tests/RoslynMcp.Tests/FlatLocationContract.cs (second and last pragma). Reason: Location is null for partial spans (build output with a start but no end), so a pure Location migration would drop positions. Implemented on release/4.x (commit 83f1629d, 54 production sites + 70 test sites, default Release build clean, 219 related tests pass). Follow-ups: locationdto-partial-span-representation-5-0, locationdto-secondary-dto-flat-field-lifecycle.
