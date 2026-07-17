# apply-undo-workflow-service-extraction — Extract apply and undo workflow service

**row:** `apply-undo-workflow-service-extraction` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ApplyUndoWorkflowService.cs` (new)
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs:23-192`
- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs:66-124`
- `tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs`
- `tests/RoslynMcp.Tests/Top10V2RegressionTests.cs`
- `tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs` (new)

## Acceptance

- [ ] A Roslyn-layer service owns project-filter derivation, pre/post diagnostic identity diffing, apply/rollback decisions, and sequence-revert invocation/reason outcomes.
- [ ] The Roslyn service returns transport-neutral domain outcomes and contains no `JsonSerializer`, anonymous wire payload, or JSON property-order concern.
- [ ] Host wrappers retain gating/validation and map the five apply plus three sequence outcomes to the exact existing property sets, order, values, nulls, and messages.
- [ ] `outputSchema` remains null; successful apply paths still use two compile legs and do not add preview consumption.
- [ ] The prerequisite cancellation behavior survives the move and DI resolves one service lifetime.

## Evidence

- Execute-time cold review separated the cancellation correctness defect from the transport/domain extraction and assigned JSON shaping to Host.

## Dependencies

- `apply-with-verify-cancelled-result-compensation`
