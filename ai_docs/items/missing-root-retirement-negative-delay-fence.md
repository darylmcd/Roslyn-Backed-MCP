# missing-root-retirement-negative-delay-fence — Replace the fixed 750 ms wait before the retirement-waits-for-readers assertion

**row:** `missing-root-retirement-negative-delay-fence` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `tests/RoslynMcp.Tests/MissingWorkspaceRootRetirementTests.cs:97`

## Acceptance

- [ ] The assertion that retirement has not completed while a read holds the gate is made after a deterministic signal (e.g. the manager root-retirement task registered while the read is held), not a sleep.
- [ ] Mutation check: letting retirement bypass the gate turns the test red.

## Evidence

- file-watcher plan same-mechanism sweep (2026-11-18): fence candidate is `GetRootRetirementTaskForTesting` registered while the read is held; shares the DI seam work of file-watcher PR1. See D:/Roslyn-Backed-MCP/.worktrees/file-watcher.report.md.
