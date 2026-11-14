# file-watcher-retire-after-ancestor-move — Retire descendant workspaces after an ancestor moves

**row:** `file-watcher-retire-after-ancestor-move` · **pri:** `Medium` · **size:** `S` · **verified:** `ddf4cc6d0f4d1ee8a165571c099820583f52eeb9`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:390`
- `tests/RoslynMcp.Tests/FileWatcherSharedWatcherTests.cs`

## Acceptance

- [ ] Moving a directory above a loaded root retires its workspace and preserves unrelated watches on Windows and Linux.

## Evidence

- RootRemoved only compares exact workspace roots. Higher ancestor moves can strand descendant sessions.

## Notes

Before selecting for 5.x, verify that retirement of an ancestor-lost workspace preserves the released workspace_list, workspace_status and workspace_close semantics for direct root loss. Qualify the existing WorkspaceRootMissing retirement path at WorkspaceManager.cs:762–790.
