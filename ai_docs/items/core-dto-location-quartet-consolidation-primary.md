# core-dto-location-quartet-consolidation-primary — Write LocationDto migration ADR + plan

**row:** `core-dto-location-quartet-consolidation-primary` · **pri:** `Medium` · **size:** `L`

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

## Decision (resolved 2026-08-06)

**Operator decision: additive serialized nested `Location` field + legacy-flat-field deprecation window, NOT a major-version replacement.** This was raised as one of two `/defer-unblock` operator questions (the other two — `http-streamable-host-project`, `roslyn-mcp-cross-repo-steering-gap` — were resolved separately); the operator picked the additive path over a breaking major bump because it avoids forcing every consumer to migrate on this repo's timeline, consistent with Directive #4's ADR + migration-note requirement for this published repo's breaking changes — additive sidesteps the break entirely for the deprecation window.

The earlier execute-time public-contract review had already rejected a `[JsonIgnore]`-only computed `Location` view (external consumers couldn't adopt it), which is why the decision needed to be explicit rather than defaulting to the path of least resistance.

## Acceptance

- [ ] Record an ADR formalizing the additive-nested-field + legacy-flat-deprecation choice (not open — see Decision above; the ADR still needs to be written as a durable artifact).
- [ ] Add migration guidance covering wire name/shape, null and partial-location semantics, the legacy-flat deprecation window, semver, record constructor compatibility, `with` expressions, and equality.
- [ ] Treat a `[JsonIgnore]`-only property as insufficient adoption.
- [ ] Split the selected migration into bounded producer/consumer groups before implementation planning.
- [ ] Keep `core-dto-location-quartet-consolidation-secondary` blocked until the primary contract is decided and its first migration stage lands.

## Evidence

- The three DTOs feed stable public tool responses.
- Direct removal currently spans at least 12 production files and 6 test files.
- `docs/release-policy.md` requires deprecation and migration handling for public breaking changes.
