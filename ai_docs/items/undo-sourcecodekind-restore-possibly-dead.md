# undo-sourcecodekind-restore-possibly-dead — Prove or delete the SourceCodeKind restore in the undo solution-diff path

**row:** `undo-sourcecodekind-restore-possibly-dead` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:734` — `undo-restore-source-kind` branch.

## Acceptance

- [ ] Either a test proves the branch changes the outcome, or it is deleted with its comment.

## Evidence

- Raised by the cold implementation review of PR 2082 (2026-11-12); unverified.

## Context

- Possible dead code; verify before deleting.
