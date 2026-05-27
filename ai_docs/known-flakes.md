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
| `RoslynMcp.Tests.ExternalEditStalenessTests.EnsureFreshForWritePreview_RefusesWithReloadHint_WhenExternalEdit` | 2026-05-20 | `Assert.Fail` "FileSystemWatcher did not flip isStale within 2000 ms of the external write" at [`WaitForStaleAsync`](../tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs#L295) line 295 | Timing-sensitive: test writes to a tracked file then polls `WorkspaceManager.IsStale` for up to 2000ms waiting for the `FileSystemWatcher` to deliver the change event. Under self-hosted CI runner load the OS-level event dispatcher can slip past the 2000ms threshold, leaving the assertion to fail with a watcher-or-dropped-event message. Evidence: failed once on PR #864 validate ([actions run 26132562106](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/26132562106), attempt 1, 2026-05-19 UTC); other 1393 tests passed; rerun (attempt 2) launched without code change. Fix-or-track: real fix is widening the 2000ms watcher threshold OR replacing the timed poll with a counter-based completion signal (e.g. event-driven `TaskCompletionSource` flipped from inside the watcher callback); track separately if recurrence becomes blocking. |
