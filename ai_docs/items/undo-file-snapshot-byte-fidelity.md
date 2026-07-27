# undo-file-snapshot-byte-fidelity — Preserve bytes and encoding across undo

**row:** `undo-file-snapshot-byte-fidelity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Services/IUndoService.cs` (`FileSnapshotDto`)
- `src/RoslynMcp.Roslyn/Services/UndoService.cs` (`RevertFromFileSnapshotsAsync`)
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs` (`BuildFileSnapshotsForSolutionChangesAsync`)
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Refactoring apply captures exact pre-apply bytes for existing files and distinguishes them from newly created files.
- [ ] Undo restores the original byte sequence, including BOM and non-UTF-8 encodings.
- [ ] Preserve the published compatibility contract; if a breaking DTO change is required, add the required ADR and `CHANGELOG.md` migration note.
- [ ] Add regressions for UTF-8 BOM and one non-UTF-8 file through apply then undo.

## Evidence

- The 2026-07-26 transactional-persistence review found that `FileSnapshotDto` stores decoded text and `UndoService` rewrites it through the default text encoding, so successful apply followed by undo can change original bytes even though transactional failure rollback is byte-exact.
