# iedit-service-param-object — Introduce shared options object for IEditService's repeated bool parameters

**row:** `iedit-service-param-object` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Services/IEditService.cs:43-51`

## Acceptance

- [ ] ApplyTextEditsAsync and ApplyMultiFileTextEditsAsync accept one shared options parameter instead of 3 duplicated bool params each
- [ ] Existing callers/implementations updated and compile clean

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02b-core-service-contracts::DG2-cleanliness
