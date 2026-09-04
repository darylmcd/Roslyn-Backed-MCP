# apply-composite-guidance-wave-2 — Migrate refactoring and scaffolding composite-apply guidance

**row:** `apply-composite-guidance-wave-2` · **pri:** `Low` · **size:** `M` · **deps:** `apply-composite-canonical-alias-surface`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestructureService.cs`
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolRefactorTools.cs`

## Acceptance

- [ ] Replace the deprecated route in the four named refactoring and scaffolding documentation surfaces.
- [ ] Preserve the distinction between composite and ordinary preview stores.
- [ ] Require scoped stale-name search to return zero in these four files.

## Evidence

These refactoring and scaffolding descriptions teach first-party consumers to call the deprecated name.

## Context

Depends on `apply-composite-canonical-alias-surface`. Migration child split from `tool-consolidation-adr-and-alias-machinery`.
