# interface-extractor-import-order — Normalize interface extractor imports

**row:** `interface-extractor-import-order` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs`
- `eng/format-baseline.json`

## Acceptance

- [ ] Normalize import ordering in both extractor services without changing symbol bindings or behavior.
- [ ] Remove only the two corresponding IMPORTS baseline entries after proving their diagnostics absent; preserve unrelated baseline inventory.
- [ ] Verify compilation and changed-file formatting with zero new findings and no remaining IMPORTS allowance for either file.

## Evidence

- Current-session admitted formatter reports one existing IMPORTS finding per extractor, baseline1 each; the initial parameter candidate had zero new findings. This row owns bounded pre-existing debt, not an allowance for new diagnostics.
