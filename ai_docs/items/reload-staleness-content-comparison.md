# reload-staleness-content-comparison — Decide mid-reload staleness by content, not file timestamps

**row:** `reload-staleness-content-comparison` · **pri:** `Low` · **size:** `M` · **verified:** `a2a51ec4de033bfd3aa36ba9d238b73e849b0f9a`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`

## Acceptance

- [ ] A mid-load edit with a preserved or coarse mtime leaves the workspace stale; a duplicate event for an already-loaded write still clears; no clock dependence in the decision.

## Evidence

- PR 2050 re-review: ClearStaleIfNoExternalEditSince survives iff the newest ExternalEdit file time >= load start; mtime-preserving writers and coarse filesystems (FAT ~2s, HFS+ 1s, network shares) fall through. Deletes/renames are timed by arrival and have no test.
