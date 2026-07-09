# scaffoldingservice-god-class-decompose — Decompose ScaffoldingService god-class

**row:** `scaffoldingservice-god-class-decompose` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:22`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs:374-443`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestPreview.cs:399-484`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TestPreview.cs:486-497`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.TypePreview.cs:305`

## Acceptance

- [ ] BuildTestContent takes a request/context object instead of 11 positional params.
- [ ] TrimUsingsToReferencedNamespaces and BuildArgExpression cyclomatic complexity drop below 15 (roslyn get_complexity_metrics).
- [ ] Scaffolding responsibilities (type/single-test/batch-test) are separated into distinct collaborators rather than one 4-file partial class.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03c-roslyn-build-test-services::DG2-cleanliness, S03c-roslyn-build-test-services::DG1-design
