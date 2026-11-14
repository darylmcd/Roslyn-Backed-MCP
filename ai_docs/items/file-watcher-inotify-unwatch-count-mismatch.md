# file-watcher-inotify-unwatch-count-mismatch — Diagnose the Linux Unwatch descriptor mismatch

**row:** `file-watcher-inotify-unwatch-count-mismatch` · **pri:** `Low` · **size:** `S` · **verified:** `573540d5b8a720c92c479993804045c78c75676c`

## Anchors

- `tests/RoslynMcp.Tests/FileWatcherSharedWatcherTests.cs:60` — process-wide descriptor assertion immediately after Unwatch.
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:402` — Unwatch ownership and disposal.

## Acceptance

- [ ] Reproduce against an unchanged Linux base with descriptor identity and owner evidence. Fix any leaked watcher, or prove the extra descriptors belong to another owner and test removal of every owned descriptor directly.

## Evidence

- Docs-only PR #2101 head 9968e157 Linux shard 4/4 run 43634218943 failed Watch_ManyWorkspacesUnderOneParent_UsesSharedAncestorInstances. `Unwatch must release every inotify instance.` Expected 9, observed 13. The docs-only head diff against base 573540d5 changes two Markdown files. Base run 43631993969 ran CodeQL only. Causality remains unknown.
- https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/43634218943/job/132567543594
