# undo-restore-source-kind — Restore SourceCodeKind on undo

**row:** `undo-restore-source-kind` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:513`

## Acceptance

- [ ] Undo restores the reviewed pre-apply SourceCodeKind; red-first test where an applied change altered the kind.
- [ ] Enumerate other restore paths using WithDocumentText only.

## Evidence

- Same mechanism family as PR 1908 (rebase/move-file); UndoService:513 was outside its clone-instance list because it restores, not clones.
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
