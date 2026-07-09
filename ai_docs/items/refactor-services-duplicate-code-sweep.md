# refactor-services-duplicate-code-sweep — Dedupe copy-pasted helper methods across refactor services

**row:** `refactor-services-duplicate-code-sweep` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RecordFieldAdditionService.cs:478`
- `src/RoslynMcp.Roslyn/Services/RecordFieldAdditionService.cs:423`
- `src/RoslynMcp.Roslyn/Services/RestructureService.cs:245`
- `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs:124`
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:670`

## Acceptance

- [ ] RestructureService and StringLiteralReplaceService share one EnumerateProjects/EnumerateDocuments implementation instead of duplicating it.
- [ ] RecordFieldAdditionService's two duplicated pairs are collapsed to single shared methods with no behavior change (existing tests pass).
- [ ] SymbolRefactorService's NormalizeMemberForPartition/NormalizeFieldForPartition are unified into one method.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03a-roslyn-refactor-services::DG2-cleanliness
