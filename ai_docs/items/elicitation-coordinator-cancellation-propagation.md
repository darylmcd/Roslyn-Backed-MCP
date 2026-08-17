# elicitation-coordinator-cancellation-propagation — Propagate cancellation through elicitation recovery

**row:** `elicitation-coordinator-cancellation-propagation` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs`
- `tests/RoslynMcp.Tests/StructuredCallElicitationCoordinatorTests.cs`

## Acceptance

- [ ] `OperationCanceledException` from direct path input, workspace-id input, workspace-load dispatch, or retry dispatch propagates unchanged.
- [ ] Non-cancellation exceptions intentionally supported by the fallback contract still return the documented null/no-recovery result.
- [ ] One table-driven phase test covers all four await boundaries and distinguishes ambient cancellation from ordinary recovery failure.
- [ ] No catch-all introduced by the MRTR migration can swallow cancellation.

## Evidence

- Four `catch (Exception)` blocks currently convert cooperative cancellation into failed recovery and the original schema-hint result.
- The outer structured tool filter already treats cancellation as a propagation invariant, so the coordinator violates its caller's contract.
