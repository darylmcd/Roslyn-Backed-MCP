---
category: Maintenance
---

- **Maintenance:** De-flaked the `WorkspaceExecutionGate` auto-reload timeout-budget tests by injecting a `TimeProvider` into the gate (optional ctor parameter, defaults to `TimeProvider.System` — production behaviour and the DI registration are unchanged) so the per-request timeout CTS, and its post-auto-reload `CancelAfter` reset, arm on a controllable clock. The three `AutoReload_ResetsTimeoutBudget*` / `AutoReload_ParallelFanout_AllReadersSucceedAfterReload` tests now drive a `FakeTimeProvider` instead of racing real `Task.Delay` calls against a real 2-second timeout — the wall-clock race that intermittently threw `TaskCanceledException` under full-suite CI CPU contention (it failed the v2.3.2 release `validate` job and needed a re-run). A mutation check confirms the rewritten tests still fail when the budget reset is removed. Closes `flaky-gate-timeout-virtual-clock`.
