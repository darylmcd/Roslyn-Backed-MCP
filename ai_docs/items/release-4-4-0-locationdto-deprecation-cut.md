# release-4-4-0-locationdto-deprecation-cut — Ship 4.4.0 (locationdto stage-3 deprecation) from release/4.x before the 5.0.0 cut

**row:** `release-4-4-0-locationdto-deprecation-cut` · **pri:** `Medium` · **size:** `M` · **deps:** `locationdto-stage3-flat-field-deprecation,release-cut-maintenance-branch-support`

## Anchors

- `src/RoslynMcp.Core/Models/SymbolDto.cs`
- `src/RoslynMcp.Core/Models/DiagnosticDto.cs`
- `src/RoslynMcp.Core/Models/TypeUsageDto.cs`
- `docs/release-policy.md`
- `CHANGELOG.md`

## Acceptance

- [ ] Branch `release/4.x` exists at tag `v4.3.0`; it carries only the stage-3 change (flat location fields `[Obsolete]` with migration text pointing to `Location`, no field removed, every in-repo consumer migrated, 0 CS0618, no suppression).
- [ ] Reflection test asserts all legacy fields are obsolete and `Location` remains the replacement; seen failing on the v4.3.0 code first.
- [ ] `v4.4.0` is tagged on that branch, `publish-nuget` is green, the package resolves on NuGet, `eng/verify-install-layers.ps1` exits 0 and the pinned `dnx` canary reports 4.4.0.
- [ ] Stage-3 commits, fragment and the `## [4.4.0]` CHANGELOG section are forward-ported to `main` (placed above `## [4.3.0]`); `main` version files stay unchanged.
- [ ] `locationdto-stage3-flat-field-deprecation` is closed in the forward-port PR.

## Evidence

- v4.3.0 contains nested `Location` on `SymbolDto`, `DiagnosticDto` and `TypeUsageDto` (`git grep Location v4.3.0 -- src/RoslynMcp.Core/Models`), so the stage-3 precondition is met.
- `docs/release-policy.md:38`: deprecate stable entries for at least one minor release before removal. `locationdto-next-major-flat-field-removal` cannot ship in 5.0.0 until a released minor carries the deprecation.
- Open sizing risk (unverified): the number of in-repo uses of the flat fields (each becomes CS0618) is not counted yet; measure before dating the cut.

## Context

Plan: `release-plan-4.4.0-then-5.0.0` Phase A. Gates `release-5-0-0-cut` through `locationdto-next-major-flat-field-removal`.

## Notes

2026-10-27 operator decision: stage 3 may use a scoped #pragma warning disable CS0618 limited to the flat-field compatibility test classes (tests that assert the legacy flat contract, e.g. LocationDtoMigrationTests), each cited to this row; no other suppression anywhere. Everything else migrates to Location. Sizing evidence: probe of [Obsolete] on the 18 flat params at v4.3.0 gave 128 unique CS0618 sites in 26 files (tests 74, RoslynMcp.Roslyn 39, Host.Stdio 15); production side is about 54 sites in 12 files.
