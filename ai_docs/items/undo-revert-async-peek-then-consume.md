# undo-revert-async-peek-then-consume — Claim the snapshot under the history lock in RevertAsync

**row:** `undo-revert-async-peek-then-consume` · **pri:** `Low` · **size:** `M` · **verified:** `e220816e3fa161caca44362acb8c70e9390e73bf`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:112` — RevertAsync peeks the slot then consumes later with no claim.
- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs:32` — only serialization today is gate.RunWriteAsync (also :87).
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] RevertAsync claims the snapshot under the history lock the way RevertBySequenceAsync does after PR #2056; a concurrent RevertBySequenceAsync cannot restore the same snapshot.
- [ ] Deterministic test (test seam, no sleeps) covering the interleaving.

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): service-level race; the host gate hides it in production.
