# undo-commit-superseded-by-newer-capture-loses-history — Older operation gets no undo history entry when a newer capture wins the pending slot

**row:** `undo-commit-superseded-by-newer-capture-loses-history` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:498-523`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs:1855`

## Acceptance

- [ ] Decision recorded (keep stale-capture-wins mirroring the CAS-lost rule, or append the entry regardless) with rationale in the type docs.
- [ ] If changed, RevertAsync_Seq0_CommitRacesUnclaim_NewerCaptureKeepsSlotAndGetsNoStaleHistory is updated to pin the new behaviour.

## Evidence

- undo-b report sketch 6 (PR 2141): the behaviour is pinned by the named test but never decided by the operator. Related existing row: undo-overlapping-captures-single-slot.
