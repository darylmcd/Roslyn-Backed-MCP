# process-exit-state-classifier-extract — Extract a process-running helper out of the executable-path resolver

**row:** `process-exit-state-classifier-extract` · **pri:** `Low` · **size:** `M` · **verified:** `96629cae1d46b5daac93e90539ff5bc624f57daa`

## Anchors

- `src/RoslynMcp.Host.Stdio/Runtime/ProcessExecutablePathResolver.cs:62`
- `src/RoslynMcp.Host.Stdio/Diagnostics/JsonLinesRetentionSweeper.cs:224`

## Acceptance

- [ ] Add an `IsProcessRunning`-style helper beside the exit-state classification in `ProcessExecutablePathResolver.cs` (or a type named for the job); `ProcessExecutablePathResolver` is typed as a path resolver but is the only exit-state classifier in the host.
- [ ] `JsonLinesRetentionSweeper.IsProcessAlive` (`JsonLinesRetentionSweeper.cs:229`, currently `ProcessExecutablePathResolver.Resolve(process).Status != ProcessExecutablePathStatus.Exited`) calls the helper; the existing `IsProcessAlive_ReportsTheCurrentProcessLiveAndAnExitedChildDead` test (`tests/RoslynMcp.Tests/JsonLinesRetentionSweeperTests.cs:391`) still passes.

## Evidence

- Seen in review of PR 2169: the sweeper reaches into a path resolver and interprets its `Exited` status to answer a liveness question.
