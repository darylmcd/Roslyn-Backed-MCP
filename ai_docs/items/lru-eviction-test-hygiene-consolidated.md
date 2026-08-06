# lru-eviction-test-hygiene-consolidated — consolidated low-severity test hygiene in the LRU-eviction characterization test

**row:** `lru-eviction-test-hygiene-consolidated` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceCapLruEvictionTests.cs:205` (`evictionCompleted.SetResult()` not in a `finally`)
- `tests/RoslynMcp.Tests/WorkspaceCapLruEvictionTests.cs:159` (unexplained 5-minute `RequestTimeout` override)

## Acceptance

- [ ] `evictionCompleted` is set in a `finally` and awaited as `await evictionCompleted.Task.WaitAsync(ct)` so a gate timeout can unwind the reader on assertion failure instead of orphaning a gate-holding task.
- [ ] A one-line comment explains why this test overrides `RequestTimeout` to 5 minutes (surviving the real MSBuild load of the second solution copy) when the other 12 gate call sites in the suite use the 2-minute default.

## Evidence

- Code-quality review of PR #1159 (`lru-eviction-concurrent-reader-safety-overstated`): if either assert before `SetResult()` fails, the gated reader lambda stays blocked forever (it ignores the gate's linked cancellation token), leaking an orphaned task holding the gate's reader lock and global-throttle slot.

## Context

Spin-off from the `lru-eviction-concurrent-reader-safety-overstated` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1159). Test-only hygiene, not a production bug.
