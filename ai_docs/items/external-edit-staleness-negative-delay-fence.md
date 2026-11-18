# external-edit-staleness-negative-delay-fence — Replace the wall-clock negative wait in the worktree-isolation staleness test with a signal fence

**row:** `external-edit-staleness-negative-delay-fence` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:613`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`

## Acceptance

- [ ] The test no longer sleeps `WatcherFlushTimeoutMs`; it awaits a marker-file callback from a test watcher obtained through a factory seam registered in TestServiceContainer, then asserts isStale is false.
- [ ] Mutation check: making the watcher flip isStale for the worktree file turns the test red regardless of scheduling.

## Evidence

- file-watcher plan same-mechanism sweep (2026-11-18): the :187/:198 sites are fixed in file-watcher PR1; :613 goes through WorkspaceManager and needs the DI-level seam, so it follows PR1 (the seam introduced there is the prerequisite; no row dep because PR1 is a rem/ branch, not a row). See D:/Roslyn-Backed-MCP/.worktrees/file-watcher.report.md.
