# test-temp-reaper-live-owner — Preserve stale directories owned by live test processes

**row:** `test-temp-reaper-live-owner` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestTempRoot.cs:62`
- `tests/RoslynMcp.Tests/TestTempRootTests.cs:84`

## Acceptance

- Reaping proves abandonment using process ownership/lifetime evidence before deleting an old sibling root; directory age alone cannot authorize deletion.
- A deterministic regression preserves an old root with a live owner while deleting an abandoned old root, including PID-reuse and unavailable-ownership evidence cases.
- Retain current-run and shared-parent protection; cleanup cannot delete another running test invocation's files.

## Evidence

- Inspected 2026-10-04: `ReapAbandonedRuns(DateTime utcNow)` checks only `if (!IsStale(candidate, utcNow))` before `TestFixtureFileSystem.DeleteDirectoryIfExists(candidate)`.
- `IsStale` compares root directory creation/write timestamps to `StaleRunAge`; writing descendant files does not establish root-level activity or prove process death.
- The fresh-sibling test covers age only. The old-sibling test permits deletion without owner evidence. A live test process can therefore lose an old root.
- Found while reverifying the addenda's claim that the reaper preserves live siblings; independent of the documentation repair.
