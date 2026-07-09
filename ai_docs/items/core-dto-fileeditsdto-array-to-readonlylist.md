# core-dto-fileeditsdto-array-to-readonlylist — Fix FileEditsDto mutable array property

**row:** `core-dto-fileeditsdto-array-to-readonlylist` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Models/FileEditsDto.cs:8`

## Acceptance

- [ ] FileEditsDto.Edits is IReadOnlyList<TextEditDto>, not an array
- [ ] Record equality for two FileEditsDto instances holding equal edit sequences returns true
- [ ] All call sites constructing/consuming FileEditsDto.Edits updated and building clean

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02a-core-dtos::DG3-robustness
