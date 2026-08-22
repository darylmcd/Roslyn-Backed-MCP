# workspace-manager-load-finalization-collaborator-extraction — extract post-open finalization

**row:** `workspace-manager-load-finalization-collaborator-extraction` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceLoadFinalizer.cs` (new)
- `tests/RoslynMcp.Tests/WorkspaceSessionLoaderFailureTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadDedupTests.cs`

## Acceptance

- [ ] Move post-open physical-path validation, project/document indexing, and readiness-state projection into one internal collaborator.
- [ ] Keep `WorkspaceManager` responsible for session coordination and the atomic old/new workspace swap, not projection details.
- [ ] Preserve the failure invariant: a finalization failure disposes the new workspace and leaves the prior loaded session observable.
- [ ] Add one regression that drives a finalization failure during reload and proves the old workspace, version, and status remain unchanged.

## Evidence

The 1,500-line `WorkspaceManager` still combines session coordination, filesystem validation, project/status projection, cache finalization, and lifecycle events. Roslyn metrics report the main `LoadAsync` at 169 lines/complexity 17 and `LoadIntoSessionAsync` at 174 lines/complexity 10. The path-canonicalization initiative added another required post-open invariant but cannot bundle a hotspot decomposition.
