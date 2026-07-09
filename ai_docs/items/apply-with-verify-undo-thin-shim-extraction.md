# apply-with-verify-undo-thin-shim-extraction — Move ApplyWithVerifyTool/UndoTools business logic into a Core service

**row:** `apply-with-verify-undo-thin-shim-extraction` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs:23-119`
- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs:85-121`
- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs:66-124`

## Acceptance

- [ ] ApplyWithVerifyTool's tool method delegates the diagnostic-diff/rollback decision tree to a Core service call
- [ ] UndoTools' reason-code mapping and RevertApplyBySequence's 3-way payload shaping move to a shared result-shaping helper outside the tool method

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04a-host-refactor-tools::DG1-design
