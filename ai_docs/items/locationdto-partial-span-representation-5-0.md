# locationdto-partial-span-representation-5-0 — Decide how 5.0 represents partial spans before the flat location fields are removed

**row:** `locationdto-partial-span-representation-5-0` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Models/LocationDto.cs`
- `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs`
- `src/RoslynMcp.Roslyn/Services/BuildService.cs`
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs`
- `docs/decisions/0001-locationdto-nested-field-migration.md`

## Acceptance

- [ ] An ADR (supersede or amend ADR 0001 per its own rule) chooses how 5.0 represents a diagnostic whose build output carries a file and a start position but no end: nullable end coordinates on `LocationDto`, a guarantee that end enrichment always fills them, or an explicit decision to drop start-only positions with a migration note.
- [ ] `locationdto-next-major-flat-field-removal` implements that choice; no start-only position is silently lost.
- [ ] A regression test feeds start-only build output (`sample.cs(2,3): error ...`) through parse, enrichment failure and serialization and asserts the position survives or the documented behavior holds.

## Evidence

- `SymbolMapper.ToOptionalLocationDto` returns null unless the path and all four coordinates are present, and `LocationDto` coordinates are non-nullable (ADR 0001 section 2: "never partially populated").
- Start-only diagnostics are a live state: `DotnetOutputParser` produces them from MSBuild output and `BuildService.EnrichDiagnosticSpansAsync` fills the end only when Roslyn can resolve it, falling back silently otherwise. `LocationDtoMigrationTests.BuildDiagnosticParsing_OnlyCreatesCompleteNestedLocations` pins `startOnly.Location == null`.
- In 4.4.0 the flat members stay the fallback through `LocationDtoFlatAccess`; removing them at 5.0 would drop those positions.

## Context

Found while implementing `locationdto-stage3-flat-field-deprecation` (operator decision 2026-10-28: one production adapter with a scoped CS0618 pragma). Blocks `locationdto-next-major-flat-field-removal` and therefore `release-5-0-0-cut`.
