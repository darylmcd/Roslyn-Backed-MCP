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

## Amendment — 2026-08-10 (backlog-sweep 20260810T175048Z, after PR #1200)

- **Producer count is NINE, per DTO — not "corrected: 8".** The Acceptance text above says "the 5 (corrected: 8, per `adr-0001-location-migration-corrections`) producer files". That is superseded. PR #1200 corrected ADR 0001 to scope the survey per DTO: **8** `DiagnosticDto` producers, **1** `SymbolDto` producer (`SymbolMapper.cs`, which double-duties as a `DiagnosticDto` producer and is the sets' only overlap), and **1** `TypeUsageDto` producer (`src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:312`) — **nine distinct files** for the three flat-field DTOs Stage 1 touches. A `DiagnosticDto`-only `rg` misses the sole `TypeUsageDto` producer, so do NOT re-derive the Stage 1 file set that way.
- **ADR anchors moved.** The `:132` / `:146` line citations above no longer resolve — PR #1200 (and its two fix cycles) shifted the Stage 1 / Stage 3 headings. Re-read the ADR's own headings rather than trusting those line numbers.
- **`LocationDto` has its own producers** (`SymbolMapper.cs`, `RecordFieldAdditionService.cs:455`), making the quartet-wide union ten files. Those need no Stage 1 change; the nine above are the migration scope. ADR constraint 2 now records this distinction.
- Stage 1 still needs its own sub-split before planning: nine producers plus three DTO files exceeds Rule 3's 4-production-file cap.
