# editorconfig-failed-write-displaces-undo-target — Preserve prior undo after a failed config edit

**row:** `editorconfig-failed-write-displaces-undo-target` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:386-396`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:79-88`

## Acceptance

- [ ] A failed second set_editorconfig_option that changes no bytes leaves the prior successful operation as the revert_last_apply target
- [ ] A failed partial write either restores the exact pre-write bytes or records a recoverable failed-write undo and change entry for bytes left on disk
- [ ] Successful config edits capture exact pre-write bytes after commit; service and undo regressions cover unchanged failure, partial failure, and subsequent revert

## Evidence

- Cold review of PR #1678: `EditorConfigService.Write` calls `CaptureBeforeApply` before parsing and writing; `UndoService.CaptureBeforeApply` immediately replaces the previous workspace snapshot. A later write failure displaces the prior successful undo target.
