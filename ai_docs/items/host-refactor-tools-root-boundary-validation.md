# host-refactor-tools-root-boundary-validation — Add ClientRootPathValidator checks to extract_type and move_type_to_file

**row:** `host-refactor-tools-root-boundary-validation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/TypeExtractionTools.cs:27`
- `src/RoslynMcp.Host.Stdio/Tools/TypeMoveTools.cs:29`

## Acceptance

- [ ] extract_type_preview/apply reject a filePath outside the validated client root(s) with a clear error
- [ ] move_type_to_file_preview/apply reject a sourceFilePath outside the validated client root(s) with a clear error

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04a-host-refactor-tools::DG5-security-data
