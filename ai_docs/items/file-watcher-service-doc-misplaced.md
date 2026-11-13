# file-watcher-service-doc-misplaced — Move the FileWatcherService XML doc onto the class

**row:** `file-watcher-service-doc-misplaced` · **pri:** `Low` · **size:** `S` · **verified:** `3582e389d56c209b67a7af896c2749c2b13b2de3`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`:8-35 — class summary/remarks precede `internal interface IEditorConfigWriteCoordinator`; the real `FileWatcherService` class has none.

## Acceptance

- [ ] The class carries the summary and remarks, the interface has its own one-line doc, and the remarks describe the single tree watcher plus shared ancestor watchers.

## Evidence

- Verified at HEAD 3582e389 after the cold review of PR 2094 flagged it.

## Context

- Doc-only; no behavior change.
