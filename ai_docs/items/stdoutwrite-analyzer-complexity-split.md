# stdoutwrite-analyzer-complexity-split — Reduce StdoutWriteAnalyzer complexity

**row:** `stdoutwrite-analyzer-complexity-split` · **pri:** `Low` · **size:** `S`

## Anchors

- `analyzers/ServerSurfaceCatalogAnalyzer/StdoutWriteAnalyzer.cs:117-215`
- `analyzers/ServerSurfaceCatalogAnalyzer/StdoutWriteAnalyzer.cs:217-268`

## Acceptance

- [ ] AnalyzeInvocation and IsConsoleErrorReceiver each drop below CC 10 after extracting the shared receiver-resolution helper
- [ ] No duplicated direct-vs-aliased-local receiver logic remains between the two methods

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S01-analyzer-catalog::DG2-cleanliness
