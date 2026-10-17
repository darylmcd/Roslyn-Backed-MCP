# test-preview-store-cross-class-eviction — Investigate cross-class token eviction in the shared test PreviewStore

**row:** `test-preview-store-cross-class-eviction` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`

## Acceptance

- [x] Determine reachability: a two-consumer TestBase regression lost its first live token after the second consumer stored 64 unrelated previews on 2026-10-17.
- [ ] Isolate the remaining concurrent preview/apply owners with production service graphs so unrelated tests cannot evict their live tokens. Preserve production limits and lifecycle invalidation; increasing the global cap is not an ownership fix.

## Evidence

- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs:102`: `services.AddSingleton(new PreviewStoreOptions());` → default `public int MaxEntries { get; init; } = 20;` (`src/RoslynMcp.Roslyn/Services/PreviewStoreOptions.cs:12`).
- `src/RoslynMcp.Core/Services/BoundedStore.cs` `EvictIfOverLimit` removes the globally oldest entries by `CreatedAt`, across workspaces: `.OrderBy(kvp => kvp.Value.CreatedAt).Take(_entries.Count - _maxEntries + 1)`.
- The DoNotParallelize audit waves moved more preview/apply classes into the parallel phase. The shared-store failure is now reproduced; RefactoringFixtureOwnershipTests passes after migration to OwnedRefactoringTestBase for Change Signature and Parameter Object. Other TestBase consumers remain to be migrated.

## Context

Raised by the cold review of `donotparallelize-audit-wave-19` (PR #1605). The stale `tests/RoslynMcp.Tests/AssemblyInfo.cs` opt-out comment half of that finding is already owned by `donotparallelize-audit-wave-40`'s acceptance.

2026-10-17 implementation evidence: a deterministic two-consumer regression against TestBase shared services failed because unrelated preview pressure evicted the first live token. OwnedRefactoringTestBase now gives Change Signature behavior classes and ParameterObjectPreviewTests independent production service graphs, with unchanged PreviewStoreOptions. The focused regression passes. Keep this row open for remaining assembly-shared preview producers; the newly split signature classes no longer depend on that shared store. Separate production capacity enforcement remains tracked by bounded-store-capacity-atomic-insertion.
