# servercatalog-analyzer-complexity-split — Simplify ServerSurfaceCatalogAnalyzer hot methods

**row:** `servercatalog-analyzer-complexity-split` · **pri:** `Low` · **size:** `S`

## Anchors

- `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs:137-179`
- `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs:206-260`

## Acceptance

- [ ] AnalyzeMethodAttributes takes a single grouped context parameter instead of three nullable INamedTypeSymbol params
- [ ] AnalyzeCatalogInvocation's filtering/binding/recording concerns are separated into distinct helper methods, each under CC 10

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S01-analyzer-catalog::DG2-cleanliness
