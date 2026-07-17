# core-dto-location-quartet-consolidation-primary — Decide public LocationDto migration contract

**row:** `core-dto-location-quartet-consolidation-primary` · **pri:** `Defer` · **size:** `L`

## Anchors

- `src/RoslynMcp.Core/Models/LocationDto.cs`
- `src/RoslynMcp.Core/Models/SymbolDto.cs:6-27`
- `src/RoslynMcp.Core/Models/DiagnosticDto.cs:6-15`
- `src/RoslynMcp.Core/Models/TypeUsageDto.cs:34-42`
- `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs`
- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs`
- `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs`
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs`
- `docs/release-policy.md`

## Decision gate

The execute-time public-contract review rejected the proposed `[JsonIgnore]` computed `Location` view: external consumers could not adopt it, so it would not satisfy the migration intent. This public repository requires an explicit compatibility and semver decision before implementation.

## Acceptance

- [ ] Record an ADR choosing either an additive serialized nested field plus legacy-flat deprecation, or a major-version replacement.
- [ ] Add migration guidance covering wire name/shape, null and partial-location semantics, the legacy-flat deprecation window, semver, record constructor compatibility, `with` expressions, and equality.
- [ ] Treat a `[JsonIgnore]`-only property as insufficient adoption.
- [ ] Split the selected migration into bounded producer/consumer groups before implementation planning.
- [ ] Keep `core-dto-location-quartet-consolidation-secondary` blocked until the primary contract is decided and its first migration stage lands.

## Evidence

- The three DTOs feed stable public tool responses.
- Direct removal currently spans at least 12 production files and 6 test files.
- `docs/release-policy.md` requires deprecation and migration handling for public breaking changes.
