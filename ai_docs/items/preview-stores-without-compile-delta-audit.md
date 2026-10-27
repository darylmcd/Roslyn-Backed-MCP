# preview-stores-without-compile-delta-audit — Audit preview producers that store tokens with no compile-error delta check

**row:** `preview-stores-without-compile-delta-audit` · **pri:** `Low` · **size:** `L` · **deps:** `extract-type-compile-gate-dependent-projects`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:53,114,172`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:159`
- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs:133`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:133`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:137`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:428`
- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:133,687`

## Acceptance

- [ ] Per producer: record whether another gate already refuses added compiler errors, with the test that proves it.
- [ ] For each producer with no gate, file a `deps`-chained row (the shared helper from `extract-type-compile-gate-dependent-projects` first) rather than fixing here.

## Evidence

- Cold review of PR #1863 (2026-10-27): these sites call `_previewStore.Store` without a diagnostics delta; others may rely on separate gates, unaudited.
