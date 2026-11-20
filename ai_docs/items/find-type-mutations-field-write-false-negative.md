# find-type-mutations-field-write-false-negative — Detect compound-assignment field writes in find_type_mutations

**row:** `find-type-mutations-field-write-false-negative` · **pri:** `Medium` · **size:** `M` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs`
- `src/RoslynMcp.Roslyn/Helpers/SharedExpressionBindings.cs`

## Acceptance

- [ ] `find_type_mutations` on `RoslynMcp.Roslyn.Helpers.SharedExpressionBindings` reports `GetDeclarationSlot` (`_syntaxSlots ??= ...`) and does not report `Matches` for a write to a local Stack.

## Evidence

- G2-F3: 2/2 locator forms.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
