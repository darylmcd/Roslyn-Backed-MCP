# Known flakes

<!-- purpose: Authoritative list of pre-existing flaky tests that subagents and the orchestrator should ignore when judging "is the build green?". Consulted by /backlog-sweep:execute Step 7 and Appendix B's failure-result parser. -->

Pre-existing flaky tests that subagents and the orchestrator should ignore when judging "is the build green?". When all failing tests match a registered pattern, the validation step treats the result as success and surfaces the count via `known flakes encountered: N` in the report.

**Discipline** (per `/backlog-sweep:execute` skill § *Known-flakes registry*):

- Subagents MUST NOT add new entries themselves — flakes go in via a dedicated PR after triage so the registry reflects real, investigated flakes, not noise.
- The orchestrator may consult this registry to override a subagent's failure verdict, but MUST NOT add entries during a sweep.
- Each entry should name the *symptom*, the *evidence*, and a *fix-or-track* disposition so future readers can decide whether to attempt the underlying fix.

## Registered flakes

| Test FQN / pattern | First seen | Symptom | Notes |
|---|---|---|---|
| `RoslynMcp.Tests.WorkspaceExecutionGateTests.AutoReload_ResetsTimeoutBudget_ToolActionGetsFullBudget` | 2026-05-17 | `TaskCanceledException` at [`WorkspaceExecutionGate.WithGlobalThrottle`](../src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs#L124) line 124 | Timing-sensitive: test asserts a 250ms tool budget resets after a 200ms simulated reload, leaving ~50ms for the action's `Task.Delay(50)`. Under self-hosted CI runner load the reload slips past 200ms, exhausting the budget. Evidence: failed first runs on PR #803 + PR #804 during sweep `20260517T025647Z` (2026-05-17); both passed on retry without code changes; PR #805 in the same wave passed first try (load varied). Fix-or-track: real fix is widening the 250ms tool budget or replacing the timing-dependent assertion with a counter-based one; track separately if recurrence becomes blocking. |
| `RoslynMcp.Tests.FileWatcherClearStaleAwaiterTests.ClearStale_ReleasesAwaiterParkedOnPriorSignal_RatherThanStrandingIt` | 2026-07-22 (first observed on PR #1034, undated in this registry until now) | `System.TimeoutException` at [`ExternalEditStalenessTests.cs:405`](../tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs#L405) — `parked.WaitAsync(TimeSpan.FromMilliseconds(UnblockBoundMs))` times out | Timing-sensitive: the test parks an awaiter on `FileWatcherService.WaitForStaleAsync`, calls `ClearStale`, then bounds the awaiter's resolution with a fixed `UnblockBoundMs` wait. Under self-hosted CI runner load the resolution slips past the bound. Evidence: PR #1034 failed on a completely unrelated diff (CodeActionTools/FlowAnalysisTools/OperationTools) and passed on an unmodified rerun; PR #1086 and PR #1085 (2026-07-22, `Directory.Packages.props`-only and `ApplyWithVerifyTool.cs`-only diffs respectively — neither touches `FileWatcherService` or this test file) both failed the same way; 3/3 local reruns of the isolated test passed in ~22ms each (self-hosted runner idle). Same class as the `WorkspaceExecutionGateTests` entry above. Fix-or-track: widen `UnblockBoundMs` or replace with a counter/signal-based assertion; tracked at `filewatcher-clearstale-timeout-flake-triage` (row stays open — only the registration half of its acceptance is done here). |
