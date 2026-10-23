# native-replay-test-concern-split — native-replay-test-concern-split

**row:** `native-replay-test-concern-split` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/WorkspaceUndoOwnershipTests.cs:26`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs:428`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs:623`
- `eng/get-test-shard-plan.ps1:4`

## Acceptance

- [ ] Group cohesive cases into discoverable classes; keep a stable old-to-new test identity map and preserve every data row/assertion.
- [ ] Keep approved deadline isolation and intrinsic undo contention; update exact targeted filters and prove adapter discovery parity.
- [ ] Share only identical fixture ownership setup; preserve specialized native authority fixtures and all required platform cases.
- [ ] Measure shard scheduling with existing unchanged gate budgets; do not claim speedup from line count or drop slow tests.

## Evidence

The 1968-line replay class combines 39 TestMethod declarations and 32 DataRow declarations across unrelated mechanisms. Class-level shard assignment cannot separate these workloads.

- Source: operator-requested full test-tree simplification audit, 2026-10-17.
- Preserve the paused batch; this row records findings, not batch advancement.

2026-10-17 implementation and focused evidence: see ai_docs/reports/20261017-test-infrastructure-simplification-audit.md, Implemented simplifications and focused verification. The report identifies completed mechanisms, remaining acceptance and exact evidence directories. Required full local/hosted qualification and shipping remain outstanding; this note does not close the row or advance the paused batch.
