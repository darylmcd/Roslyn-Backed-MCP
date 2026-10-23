# undo-reload-failure-reports-success — Retain retryable undo when workspace synchronization fails

**row:** `undo-reload-failure-reports-success` · **pri:** `Medium` · **size:** `S` · **deps:** `undo-sequence-claim-exclusivity`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:420` — File-snapshot reload/version failure is logged before returning successful disk status.
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:625` — Solution fallback has the same reload failure mechanism.
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:115` — Latest undo consumes successful pending capture/history.
- `src/RoslynMcp.Roslyn/Services/UndoService.cs:238` — Sequence undo restores its claim only on failure/cancellation.
- `tests/RoslynMcp.Tests/UndoServiceTests.cs`

## Acceptance

- [ ] Inject deterministic reload and version-restoration failures through a private workspace-manager decorator around a real isolated workspace.
- [ ] Cover latest and sequence undo through file-snapshot and solution-fallback paths, including handled exception kinds.
- [ ] Report failure while workspace/version synchronization is incomplete; retain pending capture/history for a complete retry.
- [ ] Retry through the opposite public entry point after removing the injected failure; synchronize disk/workspace/version and consume capture/history exactly once.
- [ ] Include a file created by apply: a failed undo may already delete it, but retry must still reload even when the pathname is absent.
- [ ] Observe regression failures before the fix; pass scoped and hosted checks and the serialized delivery gate before closure.

## Evidence

- Fresh read-only review at fb978941fd0704b180884d00fb027b2e92f0cfe1 found catches returning unchanged diskOk after reload failure in both restore paths.
- File-snapshot catch also encloses version restoration; changing only the returned boolean is insufficient unless partial disk restoration remains retryable.

## Context

- Independent finding from UTF-16 qualification review; not a prerequisite of PR1811, PR1814 or the restore row.
