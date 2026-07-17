# editservice-apply-verify-workflow-consolidation — Consolidate edit/apply verification workflow

**row:** `editservice-apply-verify-workflow-consolidation` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `src/RoslynMcp.Roslyn/Services/ApplyUndoWorkflowService.cs`

## Acceptance

- [ ] After the apply/undo workflow service lands, compare `EditService.RunVerifyAndMaybeRevertAsync` against it and define one shared domain-level verification primitive.
- [ ] Edit and apply entry points preserve their distinct wire contracts, preview semantics, project filters, and rollback behavior.
- [ ] Regression coverage pins pre-existing versus introduced diagnostics for both callers.

## Evidence

- Cold apply/undo review found duplicated diagnostic-baseline, verify, and revert decisions.

## Dependencies

- `apply-undo-workflow-service-extraction`
## Validation

- Pin the EditService side in `tests/RoslynMcp.Tests/ApplyTextEditVerifyTests.cs` and the apply side in `tests/RoslynMcp.Tests/Top10V2RegressionTests.cs`.
