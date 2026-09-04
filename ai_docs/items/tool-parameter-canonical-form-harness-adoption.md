# tool-parameter-canonical-form-harness-adoption — Reuse parameter enumeration harness

**row:** `tool-parameter-canonical-form-harness-adoption` · **pri:** `Low` · **size:** `S` · **deps:** `desc-budget-harness-param-family`

## Anchors

- `tests/RoslynMcp.Tests/ParameterDescriptionBudgetHarness.cs`
- `tests/RoslynMcp.Tests/ToolParameterCanonicalFormTests.cs`

## Acceptance

- [ ] `ParameterDescriptionBudgetHarness` is the sole owner of MCP tool-method and schema-parameter reflection enumeration.
- [ ] Canonical forms, load-bearing exceptions, ceiling ratchet, ordering, and site diagnostics remain unchanged.

## Regression

Change an unswept `workspaceId` description to a third form; the assembly-wide ratchet fails with the exact tool/site while declared load-bearing exceptions remain exempt.
