# workspace-restore-budget-tests-wall-clock-tolerance — Stop asserting a wall-clock tolerance in the restore-budget tests

**row:** `workspace-restore-budget-tests-wall-clock-tolerance` · **pri:** `Low` · **size:** `S` · **verified:** `b1d2a4b22f4e1105f98a08ff452be5a6c9562d24`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreBudgetTests.cs:165-171` — `AssertBudgetNear` accepts a budget within 5 seconds below the expected value; its comment says the phase clock is a real `Stopwatch`.
- `tests/RoslynMcp.Tests/WorkspaceLoadRestoreBudgetTests.cs:66-80` — `Restore_AmbientDeadlineLooserThanRestoreTimeout_KeepsRestoreTimeout`.

## Acceptance

- [ ] The budget assertions are exact against an injected clock, or otherwise independent of how long the host takes to reach the executor call.
- [ ] The tests still fail when the restore budget is computed from the wrong source (ambient deadline against restore timeout).

## Evidence

- 2026-11-18, full local gate on branch `fix/dotnet-runner-drain-output-loss`: `Restore_AmbientDeadlineLooserThanRestoreTimeout_KeepsRestoreTimeout` failed with `Expected a budget just under 00:00:45, got 00:00:39.6015866`, 0.4 seconds outside the tolerance.
- The test uses a recording executor, not the real command runner. It passed in isolation on the same binaries, in the two earlier full gates that day, and in the next full gate on the same branch (6941 passed, 0 failed).

## Context

- Not in `ai_docs/known-flakes.md`. One observation; the cause of the 5.4-second delay under gate load was not investigated.
