# script-supervisor-cleanup-failure-observability — Preserve script cleanup failures

**row:** `script-supervisor-cleanup-failure-observability` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs`
- `tests/RoslynMcp.Tests/ScriptingServiceTests.cs`

## Acceptance

- [ ] Remove the two untyped `catch { }` timer-disposal paths; rely on the idempotent timer contract or capture a narrowly justified failure.
- [ ] Treat `SemaphoreFullException` as an observable invariant violation rather than silently swallowing it; normal host disposal remains non-fatal.
- [ ] Preserve a primary execution/cancellation failure while aggregating any cleanup failure.
- [ ] One injected cleanup regression proves both the primary and cleanup diagnostics survive and capacity counters cannot drift silently.

## Evidence

`DisposeTimers` currently catches every exception without logging, and `ReleaseConcurrencySlot` suppresses `SemaphoreFullException` even though it signals double release or counter drift. A cleanup defect can therefore leave no evidence while future calls observe corrupt capacity state.
2026-08-24 timer-callback review: synchronous Timer.Dispose does not quiesce an in-flight heartbeat, so onProgress can run after ExecuteAsync returns. Periodic callbacks can also overlap and each enter before ProgressCallbackDisabled flips. Replace periodic delivery with serialized one-shot rearm or CAS ownership, await timer quiescence, and add one blocked-first-callback regression spanning multiple periods that then throws and proves exactly one callback, no post-return progress, preserved primary/cleanup diagnostics, and an exact capacity count.
2026-08-24 remediation: CAS ownership now serializes periodic progress delivery, leaves the state disabled after the first sink failure, and a blocked-first-callback regression spans multiple heartbeat periods while proving one callback and continued internal heartbeat accounting. This row remains open for quiescent timer disposal/no post-return callback, primary-plus-cleanup diagnostics, and observable semaphore invariant failures.
Adjacent remediation: the same timer-overlap regression now proves the slow-evaluation warning is also claimed atomically and emitted once; the prior non-atomic Boolean could duplicate warnings under reentrant Timer callbacks.
Contract note: ProgressHeartbeatCount reports timer ticks, while CAS may suppress overlapping sink delivery and non-quiescent disposal may allow a sink callback after the result snapshot. Current coverage therefore asserts each observable independently; define and test any stronger relationship only with the remaining timer-quiescence work.
