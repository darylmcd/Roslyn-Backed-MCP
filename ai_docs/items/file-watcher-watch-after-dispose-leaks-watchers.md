# file-watcher-watch-after-dispose-leaks-watchers — FileWatcherService Watch-after-Dispose leaks OS watchers

**row:** `file-watcher-watch-after-dispose-leaks-watchers` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:183-189`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:494-511`
- `tests/RoslynMcp.Tests/FileWatcherSharedWatcherTests.cs`

## Acceptance

- [ ] A `_disposed` flag makes Watch throw ObjectDisposedException and create no watcher; Dispose holds `_watchSync` while draining; test: Watch after Dispose throws and registers nothing, concurrent Watch/Dispose leaves no live entry.

## Evidence

- file-watcher plan bad-code sketches 1-2 (2026-11-18). Overlaps file-watcher-test-gaps-after-consolidation (Dispose test coverage); coordinate edits to FileWatcherService.cs.
