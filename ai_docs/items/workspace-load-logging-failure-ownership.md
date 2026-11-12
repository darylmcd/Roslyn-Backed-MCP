# workspace-load-logging-failure-ownership — Isolate load cleanup from logger failures

**row:** `workspace-load-logging-failure-ownership` · **pri:** `Medium` · **size:** `L` · **verified:** `a12412bbe4bd31a18cbdd8f481475587e1697c97`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadCleanupGateReleaseTests.cs`

## Acceptance

- [ ] Inject a throwing logger on the initial-load diagnostic and cleanup reporting paths. Preserve the primary failure, complete compensation, and release the workspace slot.
- [ ] Inject a throwing logger on completion and reload cleanup paths. Preserve the load-drain accounting and the outcome of the committed or failed reload.
- [ ] Retain safe diagnostic evidence through the existing protected-reporting pattern. Preserve public outcomes and published log event IDs.

## Evidence

- Cold review of PR #2078 found WorkspaceManager logging before initial-load compensation and active-load drain accounting. Cleanup reporting can also replace the primary failure or report an already committed reload as failed.
- WorkspaceSessionLoader protects reporting against a secondary throw. Current finding is source-based.
