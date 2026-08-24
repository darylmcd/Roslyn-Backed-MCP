# isolated-workspace-scope-cleanup-composition — Preserve close and fixture cleanup failures

**row:** `isolated-workspace-scope-cleanup-composition` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs`
- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBaseTests.cs`

## Acceptance

- [ ] `IsolatedWorkspaceScope.Dispose` attempts both workspace close and copied-root deletion exactly once even when either operation fails.
- [ ] Preserve one failure unchanged and aggregate close plus deletion failures without masking either diagnostic.
- [ ] Keep repeated sync/async disposal idempotent after a failed first cleanup attempt.
- [ ] Inject close and delete collaborators in focused tests covering close-only, delete-only, dual-failure, and repeated-dispose paths.

## Evidence

The scope currently marks itself disposed before calling `WorkspaceManager.Close`, then deletes its fixture only if close succeeds. A close exception skips deletion, and the disposed flag suppresses any retry. The initialization helper now composes initialization and cleanup failures correctly, but steady-state scope disposal needs the same fail-safe composition.
