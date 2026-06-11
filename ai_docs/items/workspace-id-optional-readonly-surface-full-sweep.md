# workspace-id-optional-readonly-surface-full-sweep — complete the workspaceId REQUIRED→OPTIONAL flip across the read-only surface

**row:** `workspace-id-optional-readonly-surface-full-sweep` · **pri:** `Low` · **size:** `L` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/*Tools.cs` (~45 read-only/non-destructive tool methods: `string\??\s+workspaceId`)
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (pilot pattern + `RequireResolvedWorkspaceId`)
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*.cs` (verify no per-param schema regen needed per-file)

## Acceptance

- [ ] Each flipped method gets a `[Description]` invite-omission update + the residual-case `RequireResolvedWorkspaceId` guard (mirror `SymbolTools.cs`)
- [ ] Per swept file, reflection test asserts its read-only tools expose `workspaceId` `Required:false` while mutating tools keep `Required:true`; guard throw-branch covered
- [ ] CHANGELOG "Changed" + ADR-lite (Directive #4 — published surface, backward-compatible/additive)

## Evidence

- `workspace-id-optional-readonly-surface-flip` pilot (PR #959) + its code-quality review (alias parity, medium). Source: 2026-06-09 backlog-sweep execute.

## Context

Follow-on to the shipped 3-tool pilot (`workspace-id-optional-readonly-surface-flip`, PR #959). **Gate on the pilot's `_meta.autoResolution` adoption signal** before sweeping. **Sub-batch by `*Tools.cs` file, ≤1 catalog-touching initiative per wave.**

Two known complications surfaced by the pilot:

1. **`symbol_search` (+ any tool with a REQUIRED param after `workspaceId`)** cannot take an in-place `= null` (CS1737) — needs a parameter reorder that breaks positional call sites (~16 across `SymbolSearchPaginationTests` + `Top10V3RegressionTests`), so convert those callers to **named arguments** first (or in the same sub-batch).
2. **`get_symbol_outline` alias** shares `GetDocumentSymbolsCore` with the now-optional `document_symbols` but stayed required → residual-case UX differs from its canonical twin; flip it (or document the exclusion) for parity.

Backward-compatible (explicit-id callers unaffected) → additive, but published surface → CHANGELOG "Changed" + ADR-lite (Directive #4).
