# workspace-project-index-session-lifetime — Release project indexes with workspace lifetime

**row:** `workspace-project-index-session-lifetime` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:524-546`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:982-1000`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1097-1124`

## Acceptance

- [ ] Ordinary close, root retirement/eviction and manager disposal release the matching project index and its Project/Solution snapshots.
- [ ] A lookup overlapping close cannot publish or republish a closed-session entry. Preserve version invalidation and deterministic project lookup for live sessions.
- [ ] Repeated load/lookup/close cycles do not accumulate closed-session indexes; reopened sessions cannot consume another lifetime's snapshots.
- [ ] Red-first deterministic lifecycle tests cover release, controlled publication/close races, version replacement and valid live-session cache hits. Reuse existing lifecycle seams; avoid timing-dependent garbage-collection assertions.

## Evidence

- Immutable source 0442d5fa0d4be457f0f9a5f0de50996ada720750: field982 is `private readonly ConcurrentDictionary<string, ProjectIndexEntry> _projectIndex = new(StringComparer.Ordinal);`; GetProject995 writes with `_projectIndex.AddOrUpdate(`. The field and write are its only references; no removal or clear occurs.
- CloseCore526 executes `if (!_sessions.TryRemove(workspaceId, out var session))`; lines540-544 unwatch, invalidate previews, dispose the session, release its slot and raise WorkspaceClosed. None releases the index. Dispose1113 executes `_sessions.Clear();` without clearing the index.
- Index entries retain Project instances (record984 / builder1005-1025), which retain their Solution snapshots. Source-derived retention defect; no measured memory growth or runtime reproduction claimed.
- The alias-identity change preserves one entry per workspace ID/version and the existing Project/Solution lifetime roots. Separate mechanism from selector identity and the existing isolated NuGet handle investigation.

## Context

- Re-derive all index publishers, close/retirement paths and disposal synchronization before implementing; removing entries alone is insufficient if an overlapping reader can publish afterward.
- Add focused lifecycle coverage in an existing appropriate fixture or one new fixture. Keep this row outside the current plan selection.
