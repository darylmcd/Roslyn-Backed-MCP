# analysis-services-dedup-type-traversal-helpers — Consolidate duplicated type/namespace traversal helpers in Roslyn analysis services

**row:** `analysis-services-dedup-type-traversal-helpers` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs:121-146`
- `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs:211-225`
- `src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs:352`
- `src/RoslynMcp.Roslyn/Services/ConsumerAnalysisService.cs:135-147`

## Acceptance

- [ ] A single shared type-enumeration helper exists and CouplingAnalysisService.EnumerateDeclaredTypes/EnumerateNestedTypes, SymbolSearchService.EnumerateNamedTypes, and ImpactSweepService.GetAllTypes all call it instead of duplicating the walk.
- [ ] ConsumerAnalysisService.FindContainingType and CouplingAnalysisService.FindContainingTopLevelType are unified into one shared helper.
- [ ] find_duplicated_methods no longer reports these four bodies as duplicates.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03b-roslyn-analysis-services::DG2-cleanliness (namespace/type-walk dupes)
