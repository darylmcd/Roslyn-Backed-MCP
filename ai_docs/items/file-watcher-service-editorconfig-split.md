# file-watcher-service-editorconfig-split — Split the editorconfig write coordination out of FileWatcherService.cs

**row:** `file-watcher-service-editorconfig-split` · **pri:** `Low` · **size:** `M` · **verified:** `e71aace5065e4d5a7edbefb3068e02c0a2579312`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `tests/RoslynMcp.Tests/FileWatcherSharedWatcherTests.cs`

## Acceptance

- [ ] Editorconfig coordination types live in their own file(s); RunOwnedWrite decomposed with its 10-condition boolean named; no behaviour change (existing FileWatcher*/editorconfig suites green).

## Evidence

- File-watcher PR plan sketch.
