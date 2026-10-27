# file-move-namespace-text-encoding — Preserve encoding during moved namespace rewrites

**row:** `file-move-namespace-text-encoding` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:276`
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:299`
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:307`

## Acceptance

- [ ] Carry captured Encoding and ChecksumAlgorithm through block and file-scoped namespace rewrite branches.
- [ ] Observe UTF16/BOM native move with UpdateNamespace old-red/new-green; preserve namespace/path/text/undo and no-namespace/UpdateNamespace=false controls.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Both branches return updatedRoot.GetText() with default metadata. Retained branch explicitly carries sourceText metadata; that correction is absent from current main.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
