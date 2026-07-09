# persistent-composite-storage-token-path-traversal — Sanitize PersistentCompositeStorage tokens

**row:** `persistent-composite-storage-token-path-traversal` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs:66`
- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs:105`

## Acceptance

- [ ] TryRead and Delete reject tokens that are not well-formed GUIDs (or otherwise contain path-traversal sequences) before any Path.Combine/file I/O.
- [ ] A new test proves a token containing '../' cannot read or delete a file outside _rootDirectory.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03d-roslyn-workspace-infra::DG5-security-data
