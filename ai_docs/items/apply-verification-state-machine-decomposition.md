# apply-verification-state-machine-decomposition — Decompose apply verification state machines

**row:** `apply-verification-state-machine-decomposition` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ApplyUndoWorkflowService.cs` (`ApplyWithVerifyAsync`)
- `src/RoslynMcp.Roslyn/Services/EditService.cs` (`ApplyMultiFileTextEditsAsync`, `RunVerifyAndMaybeRevertAsync`)
- `tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs`
- `tests/RoslynMcp.Tests/ApplyTextEditVerifyTests.cs`

## Acceptance

- [ ] Extract named baseline, apply, verify, cancellation-compensation, and rollback transitions without merging the distinct wire contracts.
- [ ] Reduce each named method below 80 executable lines and cyclomatic complexity 10.
- [ ] Preserve exactly-once rollback across thrown cancellation, cancelled DTO, introduced diagnostics, and explicit no-rollback paths.

## Evidence

- Live Roslyn metrics during the 2026-07-27 ten-row cold review measured 137 lines for `ApplyWithVerifyAsync`, 102 for `ApplyMultiFileTextEditsAsync`, and 83 for `RunVerifyAndMaybeRevertAsync`.
