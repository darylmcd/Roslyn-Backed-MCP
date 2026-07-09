# refactor-services-full-solution-scan-perf — Replace full-solution AST scans with indexed symbol lookups

**row:** `refactor-services-full-solution-scan-perf` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:187`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:399`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:229`

## Acceptance

- [ ] FindTypeInNamespaceAsync and CountSiblingTypesInNamespaceAsync no longer parse every document's full syntax tree on each call.
- [ ] CrossProjectRefactoringService's post-extraction scan is restricted to projects that actually reference the extracted type instead of the whole solution.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03a-roslyn-refactor-services::DG4-performance
