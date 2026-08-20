## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:160` — the ad-hoc `Revoke` call in the close-tool body
- `src/RoslynMcp.Host.Stdio/Security/RootExpansionGrantRegistry.cs:36`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:111` — `IWorkspaceManager.WorkspaceClosed`, raised at `:497` on every retire path

## Acceptance

- `RootExpansionGrantRegistry.Revoke` runs on EVERY workspace retire path — explicit close, LRU eviction (`EvictForCapAsync`), and host-shutdown `CloseAll` — by subscribing to `IWorkspaceManager.WorkspaceClosed` at host wiring; the ad-hoc `Revoke` call in the close tool is removed.
- A test loads with `expandSanctionedRoots: true`, forces LRU eviction, and asserts the grant is gone.
- The comment at `WorkspaceTools.cs:160` no longer justifies revocation with "so a later workspace id reuse cannot inherit it" — that rationale is false (`WorkspaceManager.cs:326` mints a fresh GUID per session and is the sole session-construction site). Reword to bounded grant lifetime.

## Evidence

Traced at source during the Step 8b re-review of PR #1294. `Revoke` appears only inside `WorkspaceTools.CloseWorkspace`'s write closure; `EvictForCapAsync` (`WorkspaceManager.cs:427`) and the shutdown loop (~`:894`) call `Close` directly and raise `WorkspaceClosed` (`:497`) with no registry hook.

**Leak-only, not a boundary widening** — grants key on fresh GUIDs, so a stale entry can never be inherited by a different workspace. The cost is unbounded growth of a process-lifetime static dictionary plus a lifecycle-layering smell: the registry is retired by a tool body rather than by the lifecycle event that already exists for exactly this.

Source: code-quality re-review of PR #1294, sweep 20260819T180531Z.
