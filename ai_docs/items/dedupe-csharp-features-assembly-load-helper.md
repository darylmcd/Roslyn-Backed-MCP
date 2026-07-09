# dedupe-csharp-features-assembly-load-helper — Extract shared CSharp.Features assembly-load helper

**row:** `dedupe-csharp-features-assembly-load-helper` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs:263`
- `src/RoslynMcp.Roslyn/Services/FixAllService.cs:422`

## Acceptance

- [ ] Single shared helper method implements the Assembly.Load/exception-swallow logic once
- [ ] CodeActionService.cs and FixAllService.cs both call the shared helper instead of duplicating the body

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: CSharp.Features assembly-loading helper duplicated verbatim across analysis and build/test-tooling service slices
