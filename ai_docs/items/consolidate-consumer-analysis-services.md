# consolidate-consumer-analysis-services — Consolidate overlapping consumer-analysis service interfaces

**row:** `consolidate-consumer-analysis-services` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Services/IConsumerAnalysisService.cs:22-23`
- `src/RoslynMcp.Core/Services/ITypeConsumersService.cs:30-34`

## Acceptance

- [ ] Single canonical service/method resolves "who consumes this type" queries, or the divergence is documented with a clear usage-selection guideline
- [ ] No duplicate consumer-finding implementation logic remains between the two services

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02b-core-service-contracts::DG2-cleanliness
