# file-move-namespace-text-encoding — Preserve encoding during moved namespace rewrites

**row:** `file-move-namespace-text-encoding` · **pri:** `High` · **size:** `S` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:276`
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:299`
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:307`

## Acceptance

- [ ] Carry captured Encoding and ChecksumAlgorithm through block and file-scoped namespace rewrite branches.
- [ ] Observe UTF16/BOM native move with UpdateNamespace old-red/new-green; preserve namespace/path/text/undo and no-namespace/UpdateNamespace=false controls.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Both branches return updatedRoot.GetText() with default metadata. Retained branch explicitly carries sourceText metadata; that correction is absent from current main.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
