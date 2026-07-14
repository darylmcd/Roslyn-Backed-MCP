# gatedcommandexecutor-timeout-excludes-gate-wait — command timeout starts after both gate waits

**row:** `gatedcommandexecutor-timeout-excludes-gate-wait` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs:74-84` (global + per-workspace `WaitAsync(ct)` precede `timeoutCts.CancelAfter(timeout)`)
- `tests/RoslynMcp.Tests/Services/GatedCommandExecutorTests.cs`

## Acceptance

- [ ] A caller whose command sits behind saturated gates observes bounded total latency (queue wait counted against the timeout) OR the resulting DTO/log surfaces the queue wait so operators can distinguish slow-command from gate-starvation.
- [ ] Regression test: saturate the global gate, issue a command with a short timeout, assert the chosen contract.

## Evidence

- Flagged during the 2026-07-14 CI-hang investigation (test-infra audit): under gate saturation a caller waits gate+timeout with no signal attributing the delay.

## Notes

- Timeout semantics are caller-visible (`build_workspace`/`test_run` error text names the timeout); pick the contract deliberately and update the message if queue wait joins the budget.
