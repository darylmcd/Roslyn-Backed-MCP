# workspace-cache-retention-policy - Bound persisted cache growth through an explicit retention policy

**row:** `workspace-cache-retention-policy` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheStore.cs:34`
- (new) `tests/RoslynMcp.Tests/WorkspaceCacheRetentionTests.cs`

## Acceptance

- [ ] Measure persisted entry count and size across solution, runtime and graph changes; choose an explicit retention policy from that evidence.
- [ ] Reclaim only owned cache entries under that policy, preserving concurrent readers/writers, cancellation and corrupt-entry handling.
- [ ] Verify bounded growth and failures without deleting active entries or swallowing current path-authority denials.

## Evidence

- 2026-10-22 source review observed no size/age retention implementation. Existing comment at WorkspaceCacheStore.cs:34-38 calls housekeeping a future row and assumes solution/runtime/graph counts are small without measured evidence.
- This is a separate persistence housekeeping mechanism from the active current-input and malformed-record repairs. Native workspace loading remains independent of cached graph hints.
