# workspace-manager-test-double-consolidation — share a fail-closed workspace manager test double

**row:** `workspace-manager-test-double-consolidation` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/FailClosedWorkspaceManagerStub.cs` (new)
- `tests/RoslynMcp.Tests/WorkspacePathMrtrWireTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs`

## Acceptance

- [ ] Add one reusable `IWorkspaceManager` test double whose configured `ListWorkspaces` result is explicit and whose unconfigured members fail closed with `NotSupportedException`.
- [ ] Migrate the two anchored local empty-manager copies without weakening their assertions or introducing process-global state.
- [ ] One helper regression proves the configured list is returned and an accidental unconfigured member call throws.

## Evidence

The test suite repeats full private `IWorkspaceManager` implementations in many files. The new MRTR wire regression needed another copy solely to return an empty workspace list; repeated interface boilerplate makes new interface members noisy and can let individual fakes drift to permissive behavior.
