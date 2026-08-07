# core-dto-location-quartet-stage-followups — file and scope the ADR 0001 Stage 1 and Stage 3 migration rows

**row:** `core-dto-location-quartet-stage-followups` · **pri:** `Medium` · **size:** `M`

## Anchors

- `docs/decisions/0001-locationdto-nested-field-migration.md:132` (Stage 1 reference)
- `docs/decisions/0001-locationdto-nested-field-migration.md:146` (Stage 3 reference)
- `src/RoslynMcp.Core/Models/SymbolDto.cs`
- `src/RoslynMcp.Core/Models/DiagnosticDto.cs`
- `src/RoslynMcp.Core/Models/TypeUsageDto.cs`

## Acceptance

- [ ] Stage 1 (producer-side): add the nested `Location: LocationDto?` field to `SymbolDto`/`DiagnosticDto`/`TypeUsageDto` and populate it in the 5 (corrected: 8, per `adr-0001-location-migration-corrections`) producer files ADR 0001 names, per the additive-nested-field decision.
- [ ] Stage 3 (deprecation): flip the legacy flat fields to `[Obsolete]` one minor release after Stage 1 ships, per `docs/release-policy.md`'s deprecation-window requirement; schedule removal at the next major.
- [ ] Both stages sized/split per Rule 3 (≤4 production files) before planning against them — Stage 1 alone likely exceeds the cap across 3 DTOs + 8 producers and needs its own sub-split.

## Evidence

- ADR 0001 (PR #1180) explicitly defers both stages to "(future backlog row)" at lines 132 and 146; neither stage had an open row as of the ADR's merge (`rg location-quartet ai_docs/backlog.md` returns only the now-closed primary row and the still-blocked secondary).

## Context

`core-dto-location-quartet-consolidation-primary` closed with the ADR merged (PR #1180) but explicitly scoped OUT the actual DTO-composition code change. This row exists so the deferred stages don't silently fall off the backlog. `core-dto-location-quartet-consolidation-secondary` (PropertyWriteDto/TypeMutationDto composition) is Stage 2 and already has its own row, blocked on Stage 1 landing.
