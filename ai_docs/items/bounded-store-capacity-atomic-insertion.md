# bounded-store-capacity-atomic-insertion - Enforce capacity at insertion

**row:** `bounded-store-capacity-atomic-insertion` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Services/BoundedStore.cs:26-33`
- `src/RoslynMcp.Core/Services/BoundedStore.cs:109-121`
- `tests/RoslynMcp.Tests/BoundedStoreEvictionTests.cs:15-28`

## Acceptance

- [ ] After every completed StoreEntry, retained live entries do not exceed maxEntries; the first max+1 insertion evicts the oldest eligible entry.
- [ ] Insertion and capacity enforcement form one concurrency-safe operation; simultaneous stores cannot bypass the cap or lose the newly returned token through avoidable over-eviction. Preserve TTL and invalidation behavior.
- [ ] Regression checks the boundary after each serial insertion and controlled simultaneous insertions through the shared store, with oldest/newest retention and TTL controls.

## Evidence

- Re-derived at immutable b6fea40ccefc7e967717e4f9f02053ed5a811222: StoreEntry calls `CleanExpired(); EvictIfOverLimit();` before `_entries[token] = entry;`. EvictIfOverLimit returns when `_entries.Count <= _maxEntries`, so the insertion at an exact full count deterministically retains maxEntries+1 entries. No runtime reproduction claimed.
- The ConcurrentDictionary protects individual operations, while the capacity check, enumeration, removals and insertion have no common insertion authority; concurrent insertions can independently observe capacity and exceed it. Reproduce this race before choosing the complete synchronization policy.
- Existing test stores four entries for a cap of two and asserts only after the fourth call; it misses the third-call overflow. Shared-helper repair must cover all consumers, without copying eviction policies or weakening the cap.
- This is separate from FixAll action registration, diagnostic reduction and no-token behavior. Filed outside the current selection.

- Reconciliation cold review corrected previously misreported line anchors: StoreEntry is26-33, EvictIfOverLimit is109-121, capacity guard111 at immutable b6fea40 and current a4a826dc. Historical executor sketch9 incorrectly cited128; this correction supersedes that reference. Mechanism and reproduction obligation unchanged.
