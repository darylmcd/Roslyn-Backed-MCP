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

## Re-triage against the shared-temp-root race (2026-08-10)

The registered `WorkspaceExecutionGateTests` entry above was re-checked against the fixture-destroying race fixed by row `test-temp-root-shared-cleanup-race` (`[AssemblyCleanup]` used to delete the shared `%TEMP%/RoslynMcpTests` parent, so a concurrent test assembly's in-flight fixtures vanished mid-run).

(A second entry was re-checked in the same pass; it has since been resolved and removed from this registry.)

**It is not explained by that race.** It is a different symptom class:

| | This race | The registered flake |
|---|---|---|
| Exception | `DirectoryNotFoundException` / `IOException` on a fixture path | `TaskCanceledException` |
| Failure point | Fixture *write* time, before the code under test runs | Inside a timing-bounded `await` in the code under test |
| Trigger | A sibling test-assembly process finishing first | Wall-clock slip under host load |

So the "self-hosted CI runner load" attribution **stands** — it was not a mis-attribution. Recorded here because the reverse was initially suspected during the 2026-08-10 sweep, and the suspicion was wrong: a name/among-flakes coincidence is not evidence, and the symptom signatures do not overlap. Do not fold it into that row.
