# test-concurrency-observation-atomicity — Make maximum concurrency observation atomic

**row:** `test-concurrency-observation-atomicity` · **pri:** `Medium` · **size:** `S` · **verified:** `847d7f94a4f3565da05208010a1397da04f1047f`

## Anchors

- `tests/RoslynMcp.Tests/PerformanceBehaviorTests.cs:233` (`MaxConcurrentCalls = Math.Max(...)` after an `Interlocked.Increment`)
- `tests/RoslynMcp.Tests/WorkspaceExecutionGateTests.cs:1274` (`if (n > MaxConcurrent) MaxConcurrent = n;` under the stale Volatile comment at `:1273`)

## Acceptance

- [ ] Inventory maximum updates and distinguish deliberately ordered observation paths from genuine concurrent writers; use a shared atomic/locked helper only for the identical concurrent read/modify/write mechanism.
- [ ] Use deterministic competing updates showing lost maximum on old code; preserve real request concurrency and budgets.
- [ ] Correct the stale Volatile.Read/Write comment; do not alter gate behavior.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Retained patch removes the current private tracker whose Enter compares and assigns MaxConcurrent without synchronization; shared corrected helper exists only on retained branch.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
