# core-dto-location-quartet-consolidation-primary — Compose LocationDto in SymbolDto/DiagnosticDto/TypeUsageDto

**row:** `core-dto-location-quartet-consolidation-primary` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Models/SymbolDto.cs:14-18`
- `src/RoslynMcp.Core/Models/SymbolDto.cs:6-27`
- `src/RoslynMcp.Core/Models/DiagnosticDto.cs:6-15`
- `src/RoslynMcp.Core/Models/TypeUsageDto.cs:35-39`
- `src/RoslynMcp.Core/Models/TypeUsageDto.cs:34-42`

## Acceptance

- [ ] SymbolDto, DiagnosticDto, TypeUsageDto expose a single LocationDto-typed member instead of five discrete span fields
- [ ] Existing serialization contract (JsonPropertyName output shape) is preserved or callers updated in the same change
- [ ] Solution builds clean and existing Models tests still pass

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02a-core-dtos::DG1-design, S02a-core-dtos::DG2-cleanliness
