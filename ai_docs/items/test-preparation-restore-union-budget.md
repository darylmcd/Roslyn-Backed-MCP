# test-preparation-restore-union-budget — One complete restore phase budget

**row:** `test-preparation-restore-union-budget` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.NativeProtocols.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- `tests/RoslynMcp.Tests/TestPreparationRestoreBudgetTests.cs`

## Acceptance

- [ ] Freeze selected test closures from one native graph; restore their dependency-first union once.
- [ ] Preserve evaluation globals, co-storage coalescing, package roots and before-effect authority.
- [ ] Bound input/destination capture, all restore producers and final accepted-output observations with one linked phase deadline in test preparation and host automatic restore.
- [ ] Preserve caller-cancellation precedence and host request-deadline reload reserve.
- [ ] Observe old failures for renewed budgets, duplicate dependencies and final post-producer callbacks, then pass current Windows/Linux controls.
- [ ] Audit fork restore phase placement; retain its existing linked post-producer protection.

## Evidence

- Test-preparation causal8: three genuine old failures; expected timeout/no exception twice, expected three producer calls/actual four once.
- Host RunRestoreAsync checks remaining time only before commands; captures and final AcceptSuccessfulAsync use caller cancellation.
- Fork RestoreForkAsync already links commands and post-producer observation; capture occurs before its helper and requires phase-placement review.
