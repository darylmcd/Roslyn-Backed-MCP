# structuredcalltoolfilter-god-class-decompose — Split StructuredCallToolFilter god-class

**row:** `structuredcalltoolfilter-god-class-decompose` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:77-1099`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:309-484`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:589-1077`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:1009`

## Acceptance

- [ ] Elicitation-allowlist logic lives in its own class with unit tests independent of StructuredCallToolFilter
- [ ] Metrics recording (RecordAutoResolution/RecordAutoLoadElapsed/RecordElapsed) lives in its own class, not interleaved with dispatch code
- [ ] StructuredCallToolFilter.cs LOC drops materially and its cyclomatic complexity hotspots (Create, TryElicitAndRetryAsync, TryElicitChoiceAsync, InjectMetaIntoContent) shrink or move to the extracted classes

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04e-host-server-infrastructure::DG1-design, S04e-host-server-infrastructure::DG2-cleanliness, S04e-host-server-infrastructure::DG7-config-deps-ergo (evidence 1)
