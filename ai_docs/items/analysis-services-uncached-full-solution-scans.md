# analysis-services-uncached-full-solution-scans — Cache/memoize full-solution type scans in CouplingAnalysisService and ImpactSweepService

**row:** `analysis-services-uncached-full-solution-scans` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs:66-102`
- `src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs:159-193`
- `src/RoslynMcp.Roslyn/Services/ImpactSweepService.cs:224-239`

## Acceptance

- [ ] FindMapperTypesAsync's full-solution type scan is computed once per request (cached) instead of once per dtoSiblings iteration.
- [ ] ComputeAfferentCouplingAsync's documented p50 (12807ms on the 7-project/571-doc benchmark solution) is measurably reduced against the existing 15s budget.
- [ ] No behavioral change to coupling/impact-sweep results, only performance.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03b-roslyn-analysis-services::DG4-performance
