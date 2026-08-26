# timing-sensitive-test-assertions-flake-under-load — de-flake wall-clock test assertions

**row:** `timing-sensitive-test-assertions-flake-under-load` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Services/GatedCommandExecutorTests.cs:129`
- `tests/RoslynMcp.Tests/ScriptingServiceTests.cs:312`
- `tests/RoslynMcp.Tests/CodexChangelogHookTests.cs:200`

## Acceptance

- [ ] `GatedCommandExecutorTests.ExecuteAsync_GlobalGateSaturated_CountsQueueWaitAgainstTimeout` no longer asserts `stopwatch.Elapsed < TimeSpan.FromSeconds(2)` against real wall time — use an injected clock or a load-tolerant bound.
- [ ] `ScriptingServiceTests` accepts either terminal timeout shape (the `timed out after 1 seconds` message and the watchdog `hard deadline after 3 second(s)` message) rather than pinning one side of a race.
- [ ] `CodexChangelogHookTests` hard 30s child-process timeout is raised, made configurable, or the test is restructured so contention cannot turn it into a `TimeoutException`.
- [ ] One regression shape: all three pass under a concurrent load equivalent to a parallel test run.

## Evidence

All three failed across two full local `verify-release` runs on 2026-08-25 while the self-hosted CI runner was building on the same box, and all three passed in isolation immediately afterward. Measured: `GatedCommandExecutorTests` asserted `< 2s` and observed 5.58s; `CodexChangelogHookTests` threw after 1m36s against a 30s bound.

## Context

Surfaced by executors during `/backlog-sweep:execute` for plan `20260825T151721Z`. Not caused by any diff in that sweep — the sweep merely created the contention that exposed them. Related: the sweep also recorded that running the full local gate while the self-hosted runner is active is itself a mistake (row `addenda-ci-equivalent-self-hosted-runner-caveat`); these tests are the fragility that mistake revealed.

## Amendment — 2026-08-26 (backlog-sweep 20260825T214500Z)

Two more tests joined this class, and one earlier fix was incomplete. Add to Acceptance:

- [ ] `ValidateRecentGitChangesTests.ValidateRecentGitChangesAsync_GitStatusTimeout_ReportsGitStatusUnknownNotClean` survives runner contention. Observed failing on PR #1369 — a plan-state PR whose diff is markdown and JSON only, so it provably cannot affect a git-status timeout path (CI run 32922701611, leg `docs-linux-1-of-2`).
- [ ] No test in `ScriptingServiceTests` carries an MSTest `[Timeout]` shorter than its own internal wall-clock assertion. PR #1368 raised `EvaluateAsync_InfiniteScript_TerminatesWorkerAndRecoversCapacity`'s internal bound 5s → 30s but left `[Timeout(10_000)]`, so MSTest killed the test before its assertion could fire (`timed out after 10000ms`, PR #1370, shard `windows-hosted-2-of-4`). Fixed in PR #1373; the invariant is what needs pinning, not just the constant.

**Shipped so far:** PR #1368 (bullet 2 — terminal-shape race in `ScriptingServiceTests`), PR #1373 (the MSTest-bound contradiction above). Bullets 1 (`GatedCommandExecutorTests`) and 3 (`CodexChangelogHookTests`) remain untouched.

**Frequency signal.** Draining this sweep produced four distinct load-induced flakes across four CI shards in roughly two hours, none related to the diff under test: the two `ScriptingServiceTests` shapes, this `ValidateRecentGitChanges` timeout, and `McpLoggingLifecycleWireTests` (tracked separately as `mcp-logging-wire-startup-marker-wait-race`). Parallel sweep waves are a reliable reproducer — worth using deliberately when fixing the rest of this row.
