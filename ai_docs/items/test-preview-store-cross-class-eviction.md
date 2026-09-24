# test-preview-store-cross-class-eviction — Investigate cross-class token eviction in the shared test PreviewStore

**row:** `test-preview-store-cross-class-eviction` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`

## Acceptance

- [ ] Determine whether concurrently running parallel-phase classes can evict each other's preview tokens between preview and apply in the shared 20-entry test store; record the finding.
- [ ] If reachable, size the test `PreviewStoreOptions.MaxEntries` (or scope eviction) so one class cannot evict another's live token; otherwise close as not-reachable with the evidence.

## Evidence

- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs:102`: `services.AddSingleton(new PreviewStoreOptions());` → default `public int MaxEntries { get; init; } = 20;` (`src/RoslynMcp.Roslyn/Services/PreviewStoreOptions.cs:12`).
- `src/RoslynMcp.Core/Services/BoundedStore.cs` `EvictIfOverLimit` removes the globally oldest entries by `CreatedAt`, across workspaces: `.OrderBy(kvp => kvp.Value.CreatedAt).Take(_entries.Count - _maxEntries + 1)`.
- The DoNotParallelize audit waves moved more preview/apply classes into the parallel phase; not reproduced yet.

## Context

Raised by the cold review of `donotparallelize-audit-wave-19` (PR #1605). The stale `tests/RoslynMcp.Tests/AssemblyInfo.cs` opt-out comment half of that finding is already owned by `donotparallelize-audit-wave-40`'s acceptance.
