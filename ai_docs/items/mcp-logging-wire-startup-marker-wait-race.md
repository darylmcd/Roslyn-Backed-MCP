# mcp-logging-wire-startup-marker-wait-race — de-flake the startup-marker stderr wait

## Anchors

- `tests/RoslynMcp.Tests/McpLoggingLifecycleWireTests.cs:174` (`WaitForLineAsync`)
- `tests/RoslynMcp.Tests/McpLoggingLifecycleWireTests.cs:42,83` (`ProductionHost_EmitsNoProtocolLoggingBeforeOrAfterInitialization`)

## Acceptance

- [ ] `WaitForLineAsync` cannot report "Timed out waiting for stderr marker" for a marker that is present in the stderr it captured — either it observes the queue it prints, or the diagnostic distinguishes "never emitted" from "emitted but not observed in time".
- [ ] The wait bound is load-tolerant (or driven by an awaited signal rather than a poll) so a loaded hosted runner does not fail a host that started correctly.
- [ ] One regression shape: the test passes under a concurrent load equivalent to a parallel shard run.

## Evidence

CI run 32919775492, leg `validate-leg (windows-hosted-3-of-4)`, on PR #1368 (a diff touching only `ScriptingServiceTests.cs`). The assertion read `Timed out waiting for stderr marker 'Startup surface:'` while the very same failure message printed `stderr=info: Startup[0] | Startup surface: pid=9440 version=4.0.0.0 tools=174/174/174 ... parity=ok`. The marker was emitted; the wait did not observe it. Self-refuting assertion, so this is an observation/timing defect in the helper, not a host regression.

## Context

Distinct from `timing-sensitive-test-assertions-flake-under-load` (which covers `GatedCommandExecutorTests`, `ScriptingServiceTests`, `CodexChangelogHookTests`) — different helper, different failure mode. Surfaced while draining sweep `20260825T214500Z`, whose parallel PR waves generate exactly the runner contention that exposes it.
