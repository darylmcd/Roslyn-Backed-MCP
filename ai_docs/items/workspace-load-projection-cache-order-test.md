# workspace-load-projection-cache-order-test — Pin projection-before-cache-write ordering in load finalization

**row:** `workspace-load-projection-cache-order-test` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/WorkspaceSessionLoaderFailureTests.cs`

## Acceptance

- [ ] A cache-store double proves projection happens before the cache write and a failed finalization writes no cache entry; observed red when the order is mutated.

## Evidence

- PR 1993 cold review: moving the BuildLoadProjection call after the cache block (WorkspaceManager.cs ~1465 vs ~1475) passes every test.
