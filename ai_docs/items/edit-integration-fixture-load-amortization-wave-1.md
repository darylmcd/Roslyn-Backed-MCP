# edit-integration-fixture-load-amortization-wave-1 — Reuse resettable edit fixtures

**row:** `edit-integration-fixture-load-amortization-wave-1` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs`
- `tests/RoslynMcp.Tests/ApplyTextEditVerifyTests.cs`
- `tests/RoslynMcp.Tests/EditUndoIntegrationTests.cs`

## Acceptance

- [ ] Add one class-private reset contract that restores original file bytes, workspace version, preview tokens, undo state, and open-workspace ownership before each method.
- [ ] Migrate only the two anchored suites; do not share mutable state across classes or run multiple local test processes.
- [ ] Repeatedly run each class and prove every case begins from the same clean preconditions and leaves no temp/workspace state.
- [ ] Preserve all distinct verification, rollback, encoding, disk-state, and failure assertions.

## Evidence

The edit/undo cluster has no fixed waits but repeatedly copies and loads solutions under heavy disk/MSBuild contention. `EditUndoIntegrationTests` measured about 186 seconds in the full run and 34 seconds alone; the cases are distinct, so fixture amortization is safer than test deletion.
