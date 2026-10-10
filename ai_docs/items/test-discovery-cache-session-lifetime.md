# test-discovery-cache-session-lifetime — Bound cached discovery results to workspace lifetime

**row:** `test-discovery-cache-session-lifetime` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:13-86`
- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs:85`

## Acceptance

- [ ] Closing a workspace releases its cached test-discovery DTOs; repeated load/discover/close cycles do not accumulate closed-session entries.
- [ ] Discovery already in flight when a workspace closes cannot republish a closed-session entry. Reopened sessions cannot consume stale results from an earlier lifetime.
- [ ] Preserve versioned cache behavior and cancellation; dispose any event subscriptions correctly with service lifetime.
- [ ] Red-first lifecycle tests cover ordinary close, controlled close/publication races, repeated sessions and valid cache hits after solution updates.

## Evidence

- Source inspection at immutable c087ac4f512f2ddcddd83b89aca4e3e7c611757f: singleton registration at ServiceCollectionExtensions85; TestDiscoveryService has only three _cache references (declaration18, read33, write86). No removal, WorkspaceClosed or WorkspaceReloaded handling occurs in this service.
- Source-derived retention defect; no measured growth, runtime reproduction or fix claimed by this filing. Separate mechanism from file-path identity, FQN ownership and semantic failure observability.

## Context

- Re-derive session identity/version and service shutdown contracts before implementation. Own one production service, its registration only if disposal integration requires it, and one focused lifecycle fixture; reuse an existing test seam where suitable.
