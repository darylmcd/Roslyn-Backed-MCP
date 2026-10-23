# undo-request-timer-includes-uncancelable-fixture — Decide the concurrent undo timer boundary

**row:** `undo-request-timer-includes-uncancelable-fixture` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs`: ConcurrentUndoRoutes_ClaimTheSameInverseExactlyOnce; PublishPreviousExternalUndoAsync.

## Acceptance

- [x] Operator explicitly approved the request-only 30-second timer on 2026-10-17; see ai_docs/reports/20261017-prerequisite-test-contract-decisions.md.
- [ ] If approved, start the existing 30-second CTS immediately before first RevertAsync, after fixture publication, CommitPendingCapture and version capture.
- [ ] Preserve both competing undo routes, their tokens, stage count, successful inverse, version increment, deleted file, consumed undo and unknown-sequence assertions.
- [ ] Keep all production deadlines and independent real-fork acceptance unchanged; claim no production speedup from moving this timer.
- [ ] Run both data rows and the required combined acceptance on the approved source; retain prior red evidence.

## Evidence

- Current timer starts at line118; awaited setup at128 passes CancellationToken.None through helper36-49; first timeout.Token use is RevertAsync131.
- The current boundary spends setup time without cancelling setup. Moving it extends total allowed test time by that interval despite retaining the numeric30-second timeout.
- The approved timer relocation is installed; both requests and every ownership assertion remain unchanged. Integrated validation is pending; no production latency improvement is claimed.
