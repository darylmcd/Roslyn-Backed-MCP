# Workspace Manager Cache Store Extraction Design

<!-- purpose: Decide whether cache-policy orchestration should move out of WorkspaceManager. -->
<!-- scope: in-repo -->

## Current Shape

`WorkspaceCacheStore` already owns the durable on-disk cache contract:
versioned JSON entries, temp-file writes, fail-soft read/write behavior, hashed
path segments, and direct key-based `TryGetAsync` / `PutAsync` /
`InvalidateAsync` operations. `IWorkspaceCacheStore` owns the cache key and
entry DTOs.

`WorkspaceManager` still owns the higher-level cache policy:

- cache probing before the restore-race wait;
- newest-entry enumeration under the concrete `WorkspaceCacheStore` root;
- solution content hashing, SDK version selection, and MSBuild graph hashing;
- graph and metadata-reference DTO construction from Roslyn `Solution`;
- metadata-reference freshness checks;
- cache-hit metric stamping and post-load writeback.

Those responsibilities sit near workspace lifecycle, restore readiness,
diagnostic stripping, project metadata, package restore checks, and session
eviction in a single 2,000+ line class.

## Options

1. Keep all cache policy in `WorkspaceManager`.
   - Lowest immediate churn.
   - Keeps lifecycle and load-path decisions in one class.
   - Leaves cache hashing/enumeration/freshness helpers buried in an already
     broad manager.

2. Move only persistence into `WorkspaceCacheStore`.
   - Already done.
   - Does not address the remaining policy-heavy helpers called out by the row.

3. Extract a `WorkspaceCacheCoordinator`.
   - Owns cache probing, newest-entry enumeration, cache-key material, graph
     hash comparison, metadata-reference freshness, and writeback.
   - Keeps `WorkspaceManager` responsible for session lifecycle, load locks,
     restore-race waiting, workspace replacement, stale tracking, and metrics
     orchestration.
   - Preserves `IWorkspaceCacheStore` and `WorkspaceCacheStore` as the
     persistence boundary.

## Decision

Choose option 3 as a bounded follow-on.

The extraction is justified because the current split is half-complete:
persistence is already factored, but the policy that decides when an entry is
usable still lives in `WorkspaceManager`. Moving that policy to a coordinator
reduces manager size without changing public tool contracts or on-disk cache
format.

The coordinator should be internal to `RoslynMcp.Roslyn`. It should not become a
new Core service contract in the first slice. `WorkspaceManager` can construct
or receive it as an internal collaborator, but the public `IWorkspaceManager`
shape should not change.

## Invariants

- `workspace_load` remains idempotent by path.
- Cache failures remain fail-soft and must never block cold load.
- The restore-race wait can be skipped only when metadata references are still
  stable on disk.
- Cache-hit metrics keep the same semantics: `true` only when the post-load
  graph matches a stable cached entry, `false` for cold/write paths.
- The on-disk format version and path hashing stay owned by
  `WorkspaceCacheStore`.

## Follow-On Row

`workspace-cache-coordinator-extraction` | Low | none | Extract workspace-cache probe/writeback policy from `WorkspaceManager` into an internal `WorkspaceCacheCoordinator` without changing public workspace or cache-store contracts. Anchors: `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`, new `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs`, `src/RoslynMcp.Roslyn/Services/WorkspaceCacheStore.cs`, `src/RoslynMcp.Core/Services/IWorkspaceCacheStore.cs`, `tests/RoslynMcp.Tests/Workspace/WorkspaceLoadCacheFastPathTests.cs`, `tests/RoslynMcp.Tests/Services/WorkspaceCacheStoreInvalidationTests.cs`. Regression/output shape: existing warm/cold cache fast-path tests still pass; add focused coordinator tests for cache miss, stale metadata-reference rejection, matching graph hit, graph mismatch writeback, and fail-soft store exceptions. Evidence: `ai_docs/items/workspace-manager-cache-store-extraction-design.md`.
