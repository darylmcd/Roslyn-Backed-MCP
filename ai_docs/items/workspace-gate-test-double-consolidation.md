# workspace-gate-test-double-consolidation — share a fail-closed execution-gate test double

**row:** `workspace-gate-test-double-consolidation` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/PassThroughWorkspaceExecutionGate.cs` (new)
- `tests/RoslynMcp.Tests/SamplingMrtrWireTests.cs`
- `tests/RoslynMcp.Tests/ToolDiResolutionTests.cs`

## Acceptance

- [ ] Add one reusable `IWorkspaceExecutionGate` test double whose configured read path passes through and whose unconfigured write/load members throw.
- [ ] Migrate the two anchored local copies without adding process-global mutable state.
- [ ] One helper regression proves read pass-through/cancellation and fail-closed behavior for an unconfigured member.

## Evidence

The production sampling wire regression required another private pass-through gate. Equivalent gate fakes recur across tool suites, duplicating interface boilerplate and making new interface members noisy or inconsistently permissive.
