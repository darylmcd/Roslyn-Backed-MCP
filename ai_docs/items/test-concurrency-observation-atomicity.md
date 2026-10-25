# test-concurrency-observation-atomicity — Make maximum concurrency observation atomic

**row:** `test-concurrency-observation-atomicity` · **pri:** `Medium` · **size:** `S` · **deps:** —

## Anchors

- `tests/RoslynMcp.Tests/PerformanceBehaviorTests.cs:181`
- `tests/RoslynMcp.Tests/WorkspaceExecutionGateTests.cs:975`

## Acceptance

- [ ] Inventory maximum updates and distinguish deliberately ordered observation paths from genuine concurrent writers; use a shared atomic/locked helper only for the identical concurrent read/modify/write mechanism.
- [ ] Use deterministic competing updates showing lost maximum on old code; preserve real request concurrency and budgets.
- [ ] Correct the stale Volatile.Read/Write comment; do not alter gate behavior.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Retained patch removes the current private tracker whose Enter compares and assigns MaxConcurrent without synchronization; shared corrected helper exists only on retained branch.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
