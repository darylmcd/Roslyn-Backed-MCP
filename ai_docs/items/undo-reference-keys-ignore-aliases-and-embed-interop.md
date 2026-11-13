# undo-reference-keys-ignore-aliases-and-embed-interop — Include reference aliases and EmbedInteropTypes in the undo solution-restore fail-closed comparison

**row:** `undo-reference-keys-ignore-aliases-and-embed-interop` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:685` — `ReferenceKeys` keys by path/name only.

## Acceptance

- [ ] A reference whose alias or EmbedInteropTypes changed is reported as unrestorable (red-first).

## Evidence

- Raised by the cold implementation review of PR 2082 (2026-11-12).

## Context

- Fail-closed gap, not a regression.
