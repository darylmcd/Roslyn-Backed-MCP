# editorconfig-owned-write-external-race — Prevent lost external edits during owned writes

**row:** `editorconfig-owned-write-external-race` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:375-425`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:419-429`

## Acceptance

- [ ] A competing write that completes before the held-inode recheck, honors the transaction lock, or replaces the pathname cannot be silently overwritten or reported as a successful Apply
- [ ] The owned write uses one coherent read/modify/write transaction; detected conflicts fail without recording a successful change
- [ ] Real external changes retain ExternalEdit precedence, including failed or partial owned writes
- [ ] Deterministic Windows contention and Linux same-inode/pathname interleaving regressions pass; uncooperative POSIX writers that ignore advisory locks remain tracked in editorconfig-posix-uncooperative-write-race

## Evidence

- PR #1678 cold review: `RunOwnedWrite` coordinates watcher state under an in-process reason lock, while `EditorConfigService.Write` reads bytes, constructs output, and later calls `File.WriteAllBytes`. An external writer in that interval can be overwritten; a queued event then observes owned final bytes and is suppressed as self-write. This extends the preexisting lost-update window with incorrect staleness attribution.
