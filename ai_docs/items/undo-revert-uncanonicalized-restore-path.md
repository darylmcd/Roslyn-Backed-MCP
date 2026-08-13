# undo-revert-uncanonicalized-restore-path — revert re-walks the original snapshot path

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs`
- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `src/RoslynMcp.Core/Services/IUndoService.cs`

## Acceptance

- [ ] `FileSnapshotDto` records the boundary-canonical target captured at apply time, and `RevertAsync` restores to it rather than re-walking the original snapshot path.
- [ ] One deterministic link-swap regression covers the revert direction: apply, swap the link, revert, assert the in-boundary file is restored and the swapped target is untouched.

## Evidence

Traced during the PR #1230 code-quality review: snapshots are captured against `Path.GetFullPath(filePath)` in `EditService`, and restored via `AtomicFileWriter` at `file.FilePath` in `UndoService.RevertAsync`, with no link resolution on either side. That is the same validation-to-use window the forward write was being hardened against, on the revert path — and it was NOT covered by either follow-up named in the PR #1230 changelog fragment.
