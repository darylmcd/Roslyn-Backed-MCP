# workspace-retirement-dispose-reentrancy — Re-derive synchronous disposal during retirement notifications

**row:** `workspace-retirement-dispose-reentrancy` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:645`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:668`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:670`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:675`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:825`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1179`
- `tests/RoslynMcp.Tests/MissingWorkspaceRootRetirementTests.cs`

## Acceptance

- [ ] Exercise an actual missing-root retirement whose synchronous WorkspaceClosed subscriber calls the same manager's Dispose; establish the current behavior with a bounded, owned-process regression that cannot strand the test host.
- [ ] If reproduced, make the notification/disposal lifecycle reentrancy-safe without dropping owned retirement-task shutdown, resource cleanup, or exact-once closed notifications.
- [ ] Retain ordinary close, missing-root retirement, shutdown during an active writer, and cleanup-failure ownership controls.
- [ ] Verify source/session authority cannot remain usable or publish new sessions after disposal completes.

## Evidence

| Date | Observation | Verification limit |
|---|---|---|
| 2026-10-16 | Static source chain: OnWorkspaceRootMissing registers its task; that task calls RetireMissingWorkspaceAsync, which closes the removed session and synchronously raises WorkspaceClosed. Dispose snapshots the registered tasks and synchronously joins Task.WhenAll. A subscriber invoking Dispose from that notification appears able to join the currently executing task. | No runtime reproduction yet. Treat as a concrete source risk, not a verified deadlock. |
| 2026-10-16 | The same chain existed for normal retirement before native resource-cleanup collector changes. No new Dispose subscriber was introduced by the cleanup fix. | Separate mechanism from cleanup-fault gate/capacity completion; keep this row OPEN independently. |
