# undo-audit-commit-reuses-completed-capture — Bind audit promotion to its pending capture

**row:** `undo-audit-commit-reuses-completed-capture` · **pri:** `Medium` · **size:** `S` · **deps:** `undo-explicit-empty-capture`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:313`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] Reproduce a real completed capture followed by an audit record without a new capture; the second audit must not promote the first operation's snapshot under its sequence.
- [ ] Bind promotion to a pending capture owned by that operation; cover completed, failed, cancelled and concurrent capture/record ordering without losing retryable undo.
- [ ] Retain both latest-operation and by-sequence undo contracts; a no-effect audit cannot undo an earlier real effect.
- [ ] Probe every RecordChange consumer for capture ownership; preserve successful no-op audit behavior without introducing default or compatibility paths.
- [ ] Observe old behavior red, obtain cold review and required gates, and verify merge before closure.

## Evidence

- 2026-10-23 code review: CommitPendingCapture reads _snapshots even when the stored entry already has a committed sequence, then promotes it for a new audit without proving fresh capture ownership.
- reference-rebase-noop-undo-red.log: after real apply A and a proposed capture-skipping no-op B, undo-by-B erased A's source comment; 1 failed,0 passed. The rebase PR fixes its own path by retaining a fresh capture, with an explicit regression; this separate row covers owner-level reuse by other capture-free audit callers.
- RefactoringService keeps capture and common completion semantics; no UndoService production change is included in the rebase slice.
