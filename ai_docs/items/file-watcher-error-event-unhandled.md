# file-watcher-error-event-unhandled — Recover watcher coverage after FileSystemWatcher.Error

**row:** `file-watcher-error-event-unhandled` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:207`, :342 — recursive and shared ancestor watchers.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:664-707` — workspace reload and stale clear.
- `tests/RoslynMcp.Tests/FileWatcherSharedWatcherTests.cs`

## Acceptance

- [ ] Establish the 5.x wire classification of lost or restored watcher coverage through workspace_reload and workspace_status before selection.
- [ ] A faulted tree or shared ancestor watcher marks all its subscribers stale. An unsuccessful rearm keeps them stale.
- [ ] Require restored coverage before reading a fresh snapshot. Shared-generation changes and callbacks from retired watchers preserve exact workspace and registration ownership.
- [ ] Forced activation and runtime errors exercise both watcher types. A real relevant edit is detected after successful rearm and reload.

## Evidence

- Found by the cold review of PR 2094 (2026-11-13). Pre-existing: the old per-filter watchers had no handler either. Consolidation puts all event types on one 8 KB Windows buffer, so overflow is no less likely.

## Context

- Windows buffer overflow raises Error and silently drops events; Linux inotify queue overflow behaves the same.

## Notes

2026-11-14 cold 5.x review at ddf4cc6d. Linux .NET 10 nonrecursive watchers stop on overflow. Windows has terminal errors. Reload currently clears staleness without rearming. If complete recovery requires a breaking stable response change, reopen at the next approved major with ADR, migration note and both raw protocol eras.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
