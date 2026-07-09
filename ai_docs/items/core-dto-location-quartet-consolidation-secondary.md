# core-dto-location-quartet-consolidation-secondary — Compose LocationDto in PropertyWriteDto/TypeMutationDto

**row:** `core-dto-location-quartet-consolidation-secondary` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Models/PropertyWriteDto.cs:22-26`
- `src/RoslynMcp.Core/Models/TypeMutationDto.cs:27-29`

## Acceptance

- [ ] PropertyWriteDto and MutationCallerDto compose LocationDto instead of re-declaring StartLine/StartColumn/EndLine/EndColumn
- [ ] Solution builds clean and existing Models tests still pass

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02a-core-dtos::DG1-design
