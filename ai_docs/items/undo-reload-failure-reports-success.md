# undo-reload-failure-reports-success — Retain retryable undo when workspace synchronization fails

**row:** `undo-reload-failure-reports-success` · **pri:** `Medium` · **size:** `S`

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

## Notes

### Excluded 2026-11-10 (remediation run, current-major-only)

Release impact UNCERTAIN. The complete fix turns `reverted:true` / success into `reverted:false` (revert_last_apply) or reason `revert-failed` (revert_apply_by_sequence) when workspace reload or version restoration throws. That is a stable-tool observable result change (docs/release-policy.md Compatibility Policy: a client could depend on the old success). Existing failure shapes are reused, so a minor "Fixed" is arguable but not established.

Complete scope (planner): UndoService.cs reload/RestoreVersion catches (file path and solution path), reload even when the created file is already absent, a sync marker on UndoSnapshot so a retry reloads before consuming, claim restore on any exception in RevertBySequenceAsync (currently restored only on cancellation or !reverted), tests via a virtual ReloadAsync/RestoreVersion decorator (DelegatingWorkspaceManager methods are non-virtual).

Trigger to unblock: operator/release-owner decision that an error-path success->failure correction is minor-safe (record ADR + CHANGELOG note), or the next major release. Dep undo-sequence-claim-exclusivity merged (PR #2056); edge cleared.
