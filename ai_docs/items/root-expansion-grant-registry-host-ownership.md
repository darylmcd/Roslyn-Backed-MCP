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
