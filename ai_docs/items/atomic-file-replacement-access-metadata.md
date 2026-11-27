# atomic-file-replacement-access-metadata — Preserve DACL and Unix mode on atomic file replacement

**row:** `atomic-file-replacement-access-metadata` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:277`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:201`
- `src/RoslynMcp.Roslyn/Services/EditService.cs:875`
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:579`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:617`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:625`
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:1070`

## Acceptance

- [ ] Audit every production caller of `AtomicFileWriter` overwrite paths against each operation's policy; keep byte and response behavior unchanged.
- [ ] Replace-or-overwrite paths copy or preserve existing file access metadata (Windows DACL, Unix mode/ownership) instead of inheriting only the parent directory defaults from `FileStream` `CreateNew` + `File.Move(..., overwrite: true)`.
- [ ] Platform ownership and access regression tests cover guarded and unguarded overwrite call sites without weakening atomicity or secret-safe cleanup semantics.
- [ ] After PR #2231 (`WriteGuardedReplacementAsync` with explicit `access`) merges, drop that compensation path from remaining work; it is distinct from unguarded `AtomicFileWriter` replacement.

## Evidence

- `AtomicFileWriter` (`CompositeApplyOrchestrator.cs:277-525`) reserves a same-directory temp with `FileMode.CreateNew, FileAccess.Write` (`:371-372`) and publishes via `File.Move(tmp, path, overwrite: true)` at `:514`. No step copies ACL or Unix mode from the previous destination file.
- **Unguarded overwrite callers on origin/main `3804fc5d` (re-derived 2026-11-27):**
  1. `CompositeApplyOrchestrator.cs:201` — `WriteAllTextAsync` / `WriteAllBytesAsync` on an existing path inside composite apply.
  2. `EditService.cs:875` — disk persistence after a successful edit.
  3. `ProjectMutationService.cs:579` — project mutation disk write.
  4. `UndoService.cs:617` — undo snapshot restore write.
  5. `UndoService.cs:625` — adjacent undo-path persistence in the same flow.
  6. `UndoService.cs:1070` — revert-last-apply disk write.
- **Out of scope:** `CompositeApplyOrchestrator.cs:191` (`CreateAllTextAsync`) creates a new file; creation metadata is not this defect.
- **Related, distinct work:** PR #2231 adds `WriteGuardedReplacementAsync` with explicit access handling for rollback compensation; that path is not one of the six unguarded sites above.

## Notes

Origin: pre-existing. Reach: L2 (long-lived workspaces, concurrent hosts, files with non-default ACLs). Severity: P2. Routing: I5 — backlog row only; no production change in this filing.
