# core-dto-symbollocator-validate-unit-tests — Add unit tests for SymbolLocator.Validate() branch combinatorics

**row:** `core-dto-symbollocator-validate-unit-tests` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Models/SymbolLocator.cs:51-60`

## Acceptance

- [ ] New unit tests directly exercise SymbolLocator.Validate()'s throw path and each Has* combination
- [ ] A regression in Validate() now fails a targeted unit test rather than surfacing as an obscure downstream ArgumentException

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02a-core-dtos::DG6-testability-obs
