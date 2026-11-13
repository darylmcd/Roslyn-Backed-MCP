# file-watcher-degrade-on-ancestor-watch-failure — Decide whether an unwatchable ancestor directory should fail workspace_load

**row:** `file-watcher-degrade-on-ancestor-watch-failure` · **pri:** `Low` · **size:** `S` · **verified:** `3582e389d56c209b67a7af896c2749c2b13b2de3`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs` `WatchCore` ancestor loop — any exception aborts Watch and the load.

## Acceptance

- [ ] The operator picks fail-loud or degrade; the chosen behavior is implemented with a test, and workspace_status shows reduced coverage if degraded.

## Evidence

- Cold review of PR 2094 item 9. Pre-existing behavior; operator decision because degrading means silently missing external .editorconfig edits.

## Context

- Fail-loud is the current behavior and is correct unless the operator accepts weaker staleness detection.
