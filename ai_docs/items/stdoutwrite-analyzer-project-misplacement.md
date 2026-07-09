# stdoutwrite-analyzer-project-misplacement — Move StdoutWriteAnalyzer out of the ServerSurfaceCatalog project

**row:** `stdoutwrite-analyzer-project-misplacement` · **pri:** `Low` · **size:** `M`

## Anchors

- `analyzers/ServerSurfaceCatalogAnalyzer/StdoutWriteAnalyzer.cs:52`
- `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.csproj:5`

## Acceptance

- [ ] StdoutWriteAnalyzer no longer lives in the RoslynMcp.Analyzers.ServerSurfaceCatalog assembly/namespace
- [ ] Both analyzers still build, register, and pass existing tests after the move

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S01-analyzer-catalog::DG1-design
