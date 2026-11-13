# undo-unclaim-resurrects-slot-after-clear — Stop UnclaimRevert from resurrecting an undo slot that Clear() removed mid-restore

**row:** `undo-unclaim-resurrects-slot-after-clear` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:345` — `UnclaimRevert` re-adds the claimed snapshot without checking whether `Clear()` ran after the claim.

## Acceptance

- [ ] A revert interrupted by workspace close/Clear leaves no pending slot and no history entry for that workspace (red-first test).

## Evidence

- Raised by the cold implementation review of PR 2082 (2026-11-12); not fixed there.

## Context

- Follow-up of `undo-revert-async-peek-then-consume`.
