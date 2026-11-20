# rewrite-paths-drop-document-encoding — Preserve source encoding/BOM in rewrite paths that re-wrap text with SourceText.From

**row:** `rewrite-paths-drop-document-encoding` · **pri:** `Medium` · **size:** `M` · **verified:** `847d7f94a4f3565da05208010a1397da04f1047f`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1515`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1687`
- `src/RoslynMcp.Roslyn/Services/RestructureService.cs:63`
- `src/RoslynMcp.Roslyn/Services/RestructureService.cs:117`
- `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs:124`
- Pattern to copy: `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1648` (carries `Encoding` and `ChecksumAlgorithm`); fixed sibling: `FileOperationService.TryUpdateNamespaceAsync` (PR #1862).

## Acceptance

- [ ] Each listed site builds replacement text with the existing document's `Encoding` and `ChecksumAlgorithm`.
- [ ] Per site: UTF-16 BOM document, preview + apply, assert preamble and text; observe red on old code.
- [ ] Audit and either fix or record verdicts for unchecked sites: `ExtractMethodService.cs:791-866`, `ClassSplitOrchestrator.cs:80-83`.

## Evidence

- Cold review of PR #1862 (2026-10-27): `SourceText.From(string)` yields UTF-8 no BOM, so a rewritten existing document loses its encoding. Source-grounded, not runtime-reproduced for these sites.
- New files built from a split/move of an existing document (`NamespaceRelocationService.cs:357`, `CrossProjectRefactoringService.cs:110,211,618`, `TypeMoveService.cs:119`) may need the source encoding; that is a design decision, not covered here.

## Notes

2026-11-20: re-derived at 847d7f94: the 5 SourceText.From sites still drop encoding (lines re-lined). The audit targets ExtractMethodService.cs and ClassSplitOrchestrator.cs contain no SourceText.From call at HEAD; record that verdict when fixing.
