# symbol-info-strict-mode-resolves-at-whitespace — Return NotFound from strict symbol_info at a whitespace position

**row:** `symbol-info-strict-mode-resolves-at-whitespace` · **pri:** `Medium` · **size:** `S` · **verified:** `02fddfa9500d30848b8f5e34367322db1011b3df`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:158`

## Acceptance

- [ ] Red first: `symbol_info(filePath=src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs, line=14, column=22, allowAdjacent=false)` returns NotFound; `probe_position` reports `tokenKind: Whitespace` at that position.
- [ ] Probe more whitespace shapes (leading indentation, blank line, trailing trivia) before fixing: only one position was tried.
- [ ] `allowAdjacent=true` keeps returning the adjacent symbol.

## Evidence

- Surface audit 2026-11-20 (server 5.1.1), finding G2-F4: 1/1 at the trailing trivia of the `class` keyword.
