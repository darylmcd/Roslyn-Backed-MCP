# core-dto-location-quartet-consolidation-secondary — Compose LocationDto in PropertyWriteDto/TypeMutationDto

**row:** `core-dto-location-quartet-consolidation-secondary` · **pri:** `Medium` · **size:** `M` · **deps:** `locationdto-stage1-contracts, locationdto-stage1-symbol-type-producers, locationdto-stage1-diagnostic-producers-a, locationdto-stage1-diagnostic-producers-b`

## Anchors

- `src/RoslynMcp.Core/Models/PropertyWriteDto.cs:22-26`
- `src/RoslynMcp.Core/Models/TypeMutationDto.cs:27-29`

## Acceptance

- [ ] PropertyWriteDto and MutationCallerDto compose LocationDto instead of re-declaring StartLine/StartColumn/EndLine/EndColumn
- [ ] Solution builds clean and existing Models tests still pass

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02a-core-dtos::DG1-design

## Amendment — 2026-08-10 (backlog-sweep 20260810T175048Z — selected, then DEFERRED)

- **Deferred at execute time, reason `deps-unfinished`.** This row was selected into plan `20260810T175048Z_backlog-sweep` as initiative 7 and deferred before implementation. A cold deepener verified at HEAD that ADR 0001 **Stage 1 has not shipped**: `src/RoslynMcp.Core/Models/SymbolDto.cs`, `DiagnosticDto.cs` and `TypeUsageDto.cs` carry no `Location` property.
- **The `deps` cell was misleading and has been corrected.** It named `core-dto-location-quartet-consolidation-primary`, which closed with the ADR in PR #1180 — so under the open-work-only invariant the dep read as SATISFIED and selection let this row through. The real blocker is the unfiled Stage 1 work, tracked by `core-dto-location-quartet-stage-followups`; `deps` now names that row.
- **Reconcile the shape with ADR 0001 before implementing.** This row's Acceptance says "compose LocationDto INSTEAD OF re-declaring" the flat fields — a BREAKING removal on a published surface (Directive #4). ADR 0001 chose an ADDITIVE migration: Stage 1 adds `Location: LocationDto?` alongside the flat fields, Stage 3 marks them `[Obsolete]` one minor release later. Plan the additive shape, or supersede the ADR explicitly.
