# file-move-namespace-text-encoding

**row:** `file-move-namespace-text-encoding` · **pri:** `High` · **size:** `S`

## Anchors
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:147-165`
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:291-305`

## Acceptance
- [ ] Carry captured source Encoding and ChecksumAlgorithm through both block and file-scoped namespace rewrites.
- [ ] Observe actual native Regular UTF16/BOM move with UpdateNamespace fail on old code and pass after fix; retain exact namespace/path/text and undo authority controls.
- [ ] Probe both rewrite branches; preserve no-namespace and UpdateNamespace=false behavior.

## Evidence
- Parent and cold reader independently read current source: PreviewMove captures sourceText but TryUpdateNamespace returns updatedRoot.GetText() with default metadata; added destination has no old encoding fallback.
- Scratch-only correction assigned; source25 frozen; native qualification pending.
