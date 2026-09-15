# pwsh-script-runner-post-exit-drain-budget — pwsh-script-runner-post-exit-drain-budget

**row:** `pwsh-script-runner-post-exit-drain-budget` · **pri:** `Medium` · **size:** `S`

# pwsh-script-runner-post-exit-drain-budget

## Anchors

- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs` — RunConfiguredProcessAsync and RequestTerminationAndDrainAsync.
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunnerTests.cs` — process ownership regressions.

## Acceptance

- Apply the configured timeout and caller cancellation to output draining after process exit.
- On cleanup timeout, cancel or close owned readers and observe both reader tasks before returning a bounded failure.
- Add one controlled child/descendant regression in which the parent exits while the descendant retains a redirected pipe; release the descendant in test cleanup even if an assertion fails.

## Evidence

2026-09-15 source review: RunConfiguredProcessAsync awaits Task.WhenAll(stdoutTask, stderrTask) without its waitCancellation token after WaitForExitAsync succeeds. A descendant retaining a pipe can therefore hang the test after the nominal timeout. RequestTerminationAndDrainAsync bounds its wait but leaves both reader tasks unobserved when that wait expires. The selected formatter migration uses the existing successful-kill cleanup behavior; general retained-pipe recovery needs a separate helper contract change.
