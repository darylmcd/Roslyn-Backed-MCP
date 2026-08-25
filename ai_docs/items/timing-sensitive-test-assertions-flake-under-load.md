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
