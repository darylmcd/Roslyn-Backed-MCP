# test-concurrency-observation-atomicity — test-concurrency-observation-atomicity

**row:** `test-concurrency-observation-atomicity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/PerformanceBehaviorTests.cs:181`
- `tests/RoslynMcp.Tests/WorkspaceExecutionGateTests.cs:975`

## Acceptance

- [ ] Inventory maximum-concurrency observations and distinguish deliberately ordered cases from concurrent writer paths.
- [ ] Replace racy maximum read/modify/write updates with one narrowly scoped atomic or locked observation helper; retain real competing requests and their existing budgets.
- [ ] Correct the ConcurrencyTracker comment claiming volatile operations that are absent.
- [ ] Use deterministic competing-update proof showing the old helper can lose a maximum; preserve all gate overlap and serialization assertions.

## Evidence

- PerformanceBehaviorTests increments the current count atomically but updates MaxConcurrentCalls through a non-atomic Math.Max assignment.
- WorkspaceExecutionGateTests.ConcurrencyTracker uses if (n > MaxConcurrent) MaxConcurrent = n without a lock or compare/exchange. A lower writer can overwrite a higher observation; its comment incorrectly claims Volatile.Read/Write.
- Source: full test-tree simplification audit, 2026-10-17. No runtime failure is claimed from this static finding.

2026-10-17 implementation and focused evidence: see ai_docs/reports/20261017-test-infrastructure-simplification-audit.md, Implemented simplifications and focused verification. The report identifies completed mechanisms, remaining acceptance and exact evidence directories. Required full local/hosted qualification and shipping remain outstanding; this note does not close the row or advance the paused batch.
