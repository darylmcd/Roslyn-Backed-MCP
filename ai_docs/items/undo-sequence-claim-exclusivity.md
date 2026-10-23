# undo-sequence-claim-exclusivity — Refuse a lost sequence undo claim

**row:** `undo-sequence-claim-exclusivity` · **pri:** `Medium` · **size:** `S` · **deps:** `undo-explicit-empty-capture`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:224`
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] Reproduce two concurrent attempts for the same sequence using a deterministic barrier at claim time; only the winning claimant may restore effects.
- [ ] A missing claimed index must refuse restoration and preserve another operation's current snapshot/history.
- [ ] Retain failed/cancelled winner recovery, retryability and nonoverlapping sequence undo semantics.
- [ ] Observe old behavior red, obtain fresh review and required validation, verify merge before closure.

## Evidence

- 2026-10-23 cold empty-undo plan review: RevertBySequenceAsync computes claimedIndex under the history lock but continues into restore when claimedIndex==-1, contrary to the preceding exclusivity comment.
- This concurrency mechanism predates and is independent of explicit empty capture dispatch; do not widen that correction.
