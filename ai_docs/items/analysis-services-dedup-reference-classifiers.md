# analysis-services-dedup-reference-classifiers — Unify the three independent reference-site classifiers in Roslyn analysis services

**row:** `analysis-services-dedup-reference-classifiers` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:149-210`
- `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs:171-219`
- `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:585-665`

## Acceptance

- [ ] One shared reference-site classifier implementation is used by ConsumerAnalysisService, TypeConsumersService, and MutationAnalysisService.
- [ ] Bucket/category names are reconciled to a single vocabulary (no more separate using/ctor/inherit/field/local vs TypeUsageClassification naming).
- [ ] Existing behavioral tests for all three services still pass unmodified in intent (classification outcomes unchanged).

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03b-roslyn-analysis-services::DG1-design
