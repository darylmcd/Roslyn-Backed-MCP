# composite-apply-undo-encoding-still-lossy — apply_composite_preview and UndoService solution-restore writes still drop the original BOM/encoding

**row:** `composite-apply-undo-encoding-still-lossy` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:82` (`ApplyCompositeAsync` writes with no `Encoding`)
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:488` (`diskRestores.Add((path!, oldText.ToString()))` — the same `SourceText`→`string` collapse PR #1157 removed elsewhere)
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:571`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:372` (`OriginalBytes`-absent fallback)
- `src/RoslynMcp.Core/Services/ICompositePreviewStore.cs` (`CompositeFileMutation` carries no encoding metadata)

## Acceptance

- [ ] `apply_composite_preview` writes each mutated file with its pre-apply detected encoding (thread an `Encoding`/pre-apply bytes through `CompositeFileMutation`, or resolve it at write time from the on-disk bytes before overwrite).
- [ ] `revert_last_apply`'s solution-snapshot path preserves encoding: carry `SourceText.Encoding` alongside the restore text in `diskRestores` instead of collapsing with `oldText.ToString()`, and pass it to `AtomicFileWriter.WriteAllTextAsync` (also covers the `OriginalBytes`-absent fallback).
- [ ] A test writes a UTF-8-BOM and a UTF-16 fixture, runs `apply_composite_preview` and a `revert_last_apply` that takes the solution-snapshot path, and asserts the post-write bytes keep the original preamble.

## Evidence

- Code-quality review of PR #1157 (`mutation-write-paths-drop-original-encoding`): traced at HEAD, not hypothesized — `CompositeApplyOrchestrator.cs:82` calls `AtomicFileWriter.WriteAllTextAsync` with the new `Encoding` parameter left null; `UndoService.cs:488` builds `diskRestores` via the identical `SourceText`→`string` collapse PR #1157 just removed from `EditService.PersistDocumentTextToDiskAsync`, and `:571` writes it with no encoding.

## Context

Spin-off from the `mutation-write-paths-drop-original-encoding` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1157). The shipped changelog fragment asserted this gap "is tracked separately" — this row makes that claim true.
