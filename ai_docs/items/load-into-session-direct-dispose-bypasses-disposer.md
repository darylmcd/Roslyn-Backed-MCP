# load-into-session-direct-dispose-bypasses-disposer — Route reload swap-branch disposal through the session disposer and let test helpers inject IPreviewStore

**row:** `load-into-session-direct-dispose-bypasses-disposer` · **pri:** `Low` · **size:** `S` · **verified:** `a2a51ec4de033bfd3aa36ba9d238b73e849b0f9a`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs`
- `tests/RoslynMcp.Tests/Helpers/TrackingPreviewStore.cs`

## Acceptance

- [ ] The swap branch disposes retired workspace and lease through the injectable disposer so tests can count disposals exactly once; the duplicated delegating IPreviewStore decorators (TrackingPreviewStore, ChangeSignatureTestBase, ParameterObjectPreviewTests, ThrowingInvalidationPreviewStore) share one base.

## Evidence

- PR 2049: two disposal mechanisms coexist, so "disposed exactly once" is not observable in tests; four hand-copied preview-store decorators.
