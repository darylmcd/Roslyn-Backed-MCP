# workspace-id-optional-named-argument-prerequisite — Convert positional call sites to named arguments so workspaceId can move

**row:** `workspace-id-optional-named-argument-prerequisite` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `tests/RoslynMcp.Tests/SymbolSearchPaginationTests.cs`
- `tests/RoslynMcp.Tests/Top10V3RegressionTests.cs`

## Acceptance

- [ ] Every positional call site of a tool method that has a REQUIRED parameter after `workspaceId` is converted to named arguments, so a later `= null` default does not break compilation.
- [ ] No behavior change — the conversion is mechanical and the suite stays green.

## Evidence

The pilot surfaced this concretely: `symbol_search` (and any tool with a REQUIRED param after `workspaceId`) cannot take an in-place `= null` (CS1737); it needs a parameter reorder that breaks roughly 16 positional call sites across `SymbolSearchPaginationTests` and `Top10V3RegressionTests`.

## Context

Split from `workspace-id-optional-readonly-surface-full-sweep` (2026-09-02).

**Independently actionable and NOT gated on the adoption signal** — converting positional call sites to named arguments is a pure-mechanical improvement that is correct whether or not the flip ever happens, and it removes the sharpest blocker from the flip child.
