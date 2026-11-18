# file-watcher-degrade-on-ancestor-watch-failure — Decide whether an unwatchable ancestor directory should fail workspace_load

**row:** `file-watcher-degrade-on-ancestor-watch-failure` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs` `WatchCore` ancestor loop — any exception aborts Watch and the load.

## Acceptance

- [ ] The operator picks fail-loud or degrade; the chosen behavior is implemented with a test, and workspace_status shows reduced coverage if degraded.

## Evidence

- Cold review of PR 2094 item 9. Pre-existing behavior; operator decision because degrading means silently missing external .editorconfig edits.

## Context

- Fail-loud is the current behavior and is correct unless the operator accepts weaker staleness detection.

## Notes

Remediation run 2026-11-18: premise re-verified at live code (FileWatcherService.cs WatchCore ancestor loop ~:238-265 rethrows any exception; fail-loud is live behaviour). Operator decision required. Recommended: keep fail-loud, since degrading silently drops external ancestor .editorconfig staleness detection (the defect class PR 2094 fixed). Under fail-loud the remaining work is a test locking the contract plus one remarks sentence; degrade would be a separate M PR adding a reduced-coverage flag to the watcher entry and workspace_status (additive public contract). Plan: D:/Roslyn-Backed-MCP/.worktrees/file-watcher.report.md.
