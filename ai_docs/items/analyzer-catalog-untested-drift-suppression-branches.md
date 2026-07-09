# analyzer-catalog-untested-drift-suppression-branches — Add tests for catalog-drift suppression branches

**row:** `analyzer-catalog-untested-drift-suppression-branches` · **pri:** `Medium` · **size:** `S`

## Anchors

- `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs:247-256`
- `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs:265-273`

## Acceptance

- [ ] A test exercises a non-literal catalog-name argument and asserts AddUnresolvedCatalogEntry suppresses RMCP001 for that kind
- [ ] A test asserts ReportDrift skips RMCP001 reporting when HasUnresolvedCatalogEntries is true, and reports it otherwise

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S01-analyzer-catalog::DG6-testability-obs
