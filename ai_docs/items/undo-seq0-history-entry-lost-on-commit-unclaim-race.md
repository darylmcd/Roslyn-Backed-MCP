# undo-seq0-history-entry-lost-on-commit-unclaim-race — Keep the sequence-0 undo slot's history entry when its commit races an unclaim

**row:** `undo-seq0-history-entry-lost-on-commit-unclaim-race` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:297` — uncommitted claim uses `HistoryList: null, HistoryIndex: -1`.

## Acceptance

- [ ] A commit racing an unclaim of the seq-0 slot leaves exactly one history entry (red-first, using the `AfterLifoClaim` seam).

## Evidence

- Raised by the cold implementation review of PR 2082 (2026-11-12).

## Context

- Narrow race; seam exists.
