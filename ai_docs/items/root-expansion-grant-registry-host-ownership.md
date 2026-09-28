# root-expansion-grant-registry-host-ownership — inject host-owned grant state

**row:** `root-expansion-grant-registry-host-ownership` · **pri:** `Low` · **size:** `L` · **deps:** `static-singleton-di-bypass-core-services`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs:166`
- `src/RoslynMcp.Host.Stdio/Security/RootExpansionGrantRegistry.cs:26-57`
- `src/RoslynMcp.Host.Stdio/Security/ApplyPathBoundary.cs` (added by `static-singleton-di-bypass-core-services` child 1; use the name that child lands)
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:38-98`
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:333`
- `tests/RoslynMcp.Tests/WorkspaceCapLruEvictionTests.cs:117-162`
- `tests/RoslynMcp.Tests/PreviewApplyBoundaryRevalidationTests.cs:339-422`
- `tests/RoslynMcp.Tests/Progress/ProgressEmissionTests.cs:62`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceCachePrewarmTests.cs:41-188`
- (new) `tests/RoslynMcp.Tests/RootExpansionGrantHostIsolationTests.cs`

## Acceptance

- [ ] Grant state moves off the process-static `RootExpansionGrantRegistry` onto the host-singleton boundary service that `static-singleton-di-bypass-core-services` child 1 adds. That service is the required parameter of the `IPreviewStore` overload of `ToolDispatch.ApplyByTokenAsync` and then holds both the grant state and `SecurityOptions`. No grant-only carrier is added, and neither the `ApplyByTokenAsync` signature nor its 19 call sites change again.
- [ ] `workspace_load` records grants through that injected instance, `RevalidateChangedPathsAsync` reads them from it (`ToolDispatch.cs:333`), and host composition subscribes the same instance to `IWorkspaceManager.WorkspaceClosed` in place of the static handler (`Program.cs:166`). No new DI registration is needed, because the service is already registered.
- [ ] Preserve revocation for explicit close, LRU eviction, and `CloseAll` without tool-local lifecycle logic.
- [ ] Dispose/unsubscribe ownership with the host so repeated in-process host construction cannot accumulate handlers.
- [ ] One isolation regression builds two independent hosts, grants the same synthetic workspace id in one, and proves the other host cannot observe or revoke it.

## Evidence

- Lifecycle revocation is now centralized, but authorization state remains in a process-static `ConcurrentDictionary`. Tests and embedded/multi-host processes therefore share grants across otherwise isolated host lifetimes, and the event subscription has no injected owner to unsubscribe.
- Re-derived at `716b5e20` by the second PR #1665 review:
  - The static has three production consumers: `WorkspaceTools.cs:80` (`Grant`, inside `LoadWorkspace`), `ToolDispatch.cs:333` (`IsGranted`, inside `RevalidateChangedPathsAsync`) and `Program.cs:166` (`Revoke` subscribed to `WorkspaceClosed`).
  - `RevalidateChangedPathsAsync` has one caller, the `IPreviewStore` overload of `ApplyByTokenAsync`, which has 19 call sites in 12 `*Tools.cs` files (list in `static-singleton-di-bypass-core-services` Evidence).
  - Tests that touch the static: `WorkspaceCapLruEvictionTests.cs:117`, `:143`, `:157`, `:162` and `PreviewApplyBoundaryRevalidationTests.cs:339`, `:352`, `:366`, `:396`, `:422`.
  - Three test files call `WorkspaceTools.LoadWorkspace` directly, so a new `workspace_load` parameter reaches all of them: WorkspaceCapLruEvictionTests (`:130`, `:146`), `Workspace/WorkspaceCachePrewarmTests` (`:41`, `:89`, `:130`, `:160`, `:188`) and `Progress/ProgressEmissionTests` (`:62`).

## Notes

- Shared carrier with `static-singleton-di-bypass-core-services` (split child 1, SecurityOptionsSnapshot), added by the PR #1665 review. The dep on that row makes child 1 land first. Child 1 adds the boundary service and owns the 12-file `ApplyByTokenAsync` fan-out, and this row only adds grant state to it. When that row is split, re-point this dep to child 1's id. Closing the parent id without re-pointing would unblock this row before the service exists.
- Size L by anchor count (5 production, 5 test), re-derived by the second PR #1665 review. Keep it whole; this is the forcing shape to cite in Scope. The grant writer (`workspace_load`), reader (`RevalidateChangedPathsAsync`) and revoker (the `WorkspaceClosed` subscription) must move to the instance in one change. Otherwise grants are written to one store and read from another, and every apply on an expansion-loaded workspace is refused. `ProgressEmissionTests` and `WorkspaceCachePrewarmTests` each take one compile-forced argument for the new `workspace_load` parameter. The schema test that child 1 adds to `ToolDiResolutionTests` already covers that parameter.
