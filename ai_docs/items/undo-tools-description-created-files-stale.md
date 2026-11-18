# undo-tools-description-created-files-stale — Correct revert_last_apply description: created files are deleted

**row:** `undo-tools-description-created-files-stale` · **pri:** `Medium` · **size:** `M`


## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs`

## Acceptance

- [ ] Description states created files are deleted on revert.
- [ ] Surface snapshot and catalog tests regenerated and green.

## Evidence

wb read UndoService.cs:462-470 and CHANGELOG.md:2242; ai_docs/domains/tool-usage-guide.md corrected in the same audit. (doc-audit 2026-11-17)
