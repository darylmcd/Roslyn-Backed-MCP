# workspace-session-dispose-unchained-disposal — Chain WorkspaceSession.Dispose so a throwing Workspace/lease dispose cannot skip the rest or leak LoadLock

**row:** `workspace-session-dispose-unchained-disposal` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` (WorkspaceSession.Dispose, nested private class)
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs`

## Acceptance

- [ ] Workspace, AnalyzerLease and LoadLock are each disposed even when an earlier disposal throws; failures aggregate and the first is not masked.
- [ ] Red-first test observes LoadLock disposal and lease disposal after a throwing workspace dispose, through an internal accessor or an extracted swap-and-retire seam (WorkspaceSession is private nested and MSBuildWorkspace is sealed, so no seam-free test exists).

## Evidence

- Same mechanism as workspace-native-load-cleanup-gate-release (PR for that row left this out: no observable seam, Directive #7). Sequential Dispose calls at the class cited above.
