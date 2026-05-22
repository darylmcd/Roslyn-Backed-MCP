# Scripting Service Runtime State Extraction Design

<!-- purpose: Decide whether ScriptingService runtime-state extraction reduces risk. -->
<!-- scope: in-repo -->

## Current Shape

`ScriptingService` owns the public `IScriptingService.EvaluateAsync` behavior
and the watchdog contract for Roslyn script execution. The hard safety model is
compact but tightly coupled:

- one dedicated background worker thread per evaluation;
- budget token plus hard deadline timer;
- heartbeat timer and progress callback;
- active-evaluation and abandoned-worker counters;
- concurrency slot acquire/release;
- abandoned-thread cap refusal;
- DTO construction for success, compilation failure, runtime failure, timeout,
  hard deadline, and outer cancellation.

The tests in `tests/RoslynMcp.Tests/ScriptingServiceTests.cs` cover the hard
deadline path, abandoned-thread cap, later successful evaluation after an
abandoned worker, and heartbeat reporting.

## Options

1. Extract execution coordination now.
   - Possible boundary: worker-thread startup, timers, completion race, and
     final outcome mapping.
   - High risk because the deadline race, slot release, abandoned-counter
     recovery, and timer disposal are one correctness unit.

2. Extract runtime-state accounting only.
   - Possible boundary: active/abandoned counters plus capacity refusal.
   - Reduces a small amount of field-level noise, but the call sites still need
     to interleave accounting with worker completion and deadline decisions.

3. Keep the implementation together and treat the current comments/tests as the
   safety boundary.
   - Lowest risk.
   - Avoids a new abstraction around highly timing-sensitive code.
   - Leaves the class broad, but not obviously misfactored: most methods serve
     one watchdog/evaluation state machine.

## Decision

Reject extraction for now.

The current cohesion complaint is real, but the risky responsibilities are
coupled by correctness, not accidental placement. The service is easier to
audit while the state machine remains visible in one file. A premature
coordinator split would make the interlocked flag and timer ownership harder to
reason about.

## Future Trigger

Reopen this only if one of these happens:

- a new scripting feature requires another independent evaluation mode;
- watchdog bugs recur in slot release, abandoned-worker recovery, or timer
  disposal;
- `ScriptingService` grows new responsibilities unrelated to evaluation
  execution, such as persistence, package resolution, or multi-language script
  dispatch.

## Tests Required For Any Future Extraction

Any future extraction must preserve or add tests for:

- infinite CPU loop returns on hard deadline within budget plus grace;
- abandoned-worker cap refuses new work with an actionable message;
- a later normal script succeeds after an earlier abandoned worker;
- heartbeat callback count and DTO heartbeat count remain consistent;
- outer cancellation returns promptly and releases the concurrency slot.

## Follow-On

No follow-on implementation row is justified from the current evidence.
