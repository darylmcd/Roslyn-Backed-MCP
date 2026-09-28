# root-expansion-grant-registry-host-ownership — inject host-owned grant state

**row:** `root-expansion-grant-registry-host-ownership` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- `src/RoslynMcp.Host.Stdio/Security/RootExpansionGrantRegistry.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs`
- `tests/RoslynMcp.Tests/WorkspaceCapLruEvictionTests.cs`
- New focused host-isolation test.

## Acceptance

- [ ] Replace the process-static registry with one injected host singleton; inject it into load and apply consumers, and subscribe that instance to `IWorkspaceManager.WorkspaceClosed` during host composition.
- [ ] Preserve revocation for explicit close, LRU eviction, and `CloseAll` without tool-local lifecycle logic.
- [ ] Dispose/unsubscribe ownership with the host so repeated in-process host construction cannot accumulate handlers.
- [ ] One isolation regression builds two independent hosts, grants the same synthetic workspace id in one, and proves the other host cannot observe or revoke it.

## Evidence

Lifecycle revocation is now centralized, but authorization state remains in a process-static `ConcurrentDictionary`. Tests and embedded/multi-host processes therefore share grants across otherwise isolated host lifetimes, and the event subscription has no injected owner to unsubscribe.

## Notes

- Shared carrier with `static-singleton-di-bypass-core-services` (split child 1, SecurityOptionsSnapshot), added by the PR #1665 review. The apply-side consumer is `ToolDispatch.RevalidateChangedPathsAsync` (`ToolDispatch.cs:333`). Its only caller is the `IPreviewStore` overload of `ToolDispatch.ApplyByTokenAsync`, which has 19 call sites in 12 `*Tools.cs` files at `716b5e20` (list in that row's Evidence).
- Build one host-singleton boundary service that holds both the grant state and `SecurityOptions`, passed as a required parameter of that overload. The first of this row and that child to land adds the service and owns the 12-file fan-out, which makes this row effective L if it goes first. The second adds its state to the existing service and has no fan-out.
