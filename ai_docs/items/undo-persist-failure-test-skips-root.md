# undo-persist-failure-test-skips-root — Make the persist-failure undo test independent of file permissions

**row:** `undo-persist-failure-test-skips-root` · **pri:** `Low` · **size:** `S` · **verified:** `e220816e3fa161caca44362acb8c70e9390e73bf`

## Anchors

- `tests/RoslynMcp.Tests/UndoServiceTests.cs:600` — PersistFailure_LeavesPendingCapture_AuditDoesNotAdoptIt_RevertAsyncRestores relies on a read-only file making TryApplyChanges throw UnauthorizedAccessException.

## Acceptance

- [ ] Test passes or is Assert.Inconclusive when run as root (container), or uses non-permission failure injection.

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): as root, permissions are bypassed and the write succeeds, so the ThrowsAsync assertion fails.
