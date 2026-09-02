# get-symbol-outline-workspace-id-alias-parity — get_symbol_outline / document_symbols residual-case parity

**row:** `get-symbol-outline-workspace-id-alias-parity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`

## Acceptance

- [ ] `get_symbol_outline` either flips to an optional `workspaceId` matching its canonical twin `document_symbols`, or the exclusion is documented with the reason.
- [ ] A regression asserts the two aliases agree on residual-case behavior.

## Evidence

The pilot left `get_symbol_outline` required while `document_symbols` became optional, even though both share `GetDocumentSymbolsCore` — so two names over one implementation now behave differently when `workspaceId` is omitted.

## Context

Split from `workspace-id-optional-readonly-surface-full-sweep` (2026-09-02).

**Independently actionable and NOT gated on the adoption signal** — this is an existing inconsistency between two aliases over one core, not new surface. Fixing it is correct regardless of whether the full sweep proceeds.
