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
## Amendment (2026-07-23, from apply-with-verify-complete-diagnostic-baseline / PR #1116 code-quality review)

The `apply-with-verify-complete-diagnostic-baseline` initiative closed the Limit:50 default-pagination truncation hazard for `ApplyUndoWorkflowService.ApplyWithVerifyAsync` (now requests the complete error-identity set, `Limit:int.MaxValue`, per leg). The same hazard exists on the EditService verify path at a larger threshold:

- `EditService.cs:745`'s post-check uses `Limit:500`; `CompileCheckService.CheckAsync:64` materializes `acc.Diagnostics` then `Skip(offset).Take(limit)`, so an introduced error sorting past index 500 in the post-edit page is excluded from `postCheck.Diagnostics` and never added to `newDiagnostics` — the identical bug at a 500- instead of 50-error threshold.
- `EditService.cs:702-705`'s comment claims `>500` errors is "not a correctness hazard" — that reasoning covers only baseline over-counting and misses this post-edit truncation.

Updated acceptance for this row's consolidation work:

- [ ] The shared verification primitive requests the COMPLETE error set (`Limit:int.MaxValue`, matching `ApplyUndoWorkflowService`) so an introduced error sorting past index 500 in the post-edit page cannot be truncated out of the diffed set.
- [ ] Correct/remove the `EditService.cs:702-705` comment claiming `>500` errors is "not a correctness hazard".
- [ ] Regression test pins an introduced error beyond the page boundary for the EditService `verify=true` path (mirror `ApplyWithVerifyCancellationAndScopeTests.Verify_RegressionBeyondDefaultPage_IsCaught_AndRolledBack`).

Source: code-quality-reviewer finding on PR #1116 (backlog-sweep plan `20260723T025555Z_backlog-sweep`, initiative `apply-with-verify-complete-diagnostic-baseline`).
