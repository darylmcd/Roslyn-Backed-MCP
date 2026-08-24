# scripting-killable-worker-isolation-boundary — Reclaim non-cooperative scripts

**row:** `scripting-killable-worker-isolation-boundary` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScriptingServiceOptions.cs`
- `src/RoslynMcp.Roslyn/Services/ScriptingService.cs`
- `src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs`
- `tests/RoslynMcp.Tests/ScriptingServiceTests.cs`

## Acceptance

- [ ] Run user C# in an owned worker process that can be terminated after budget plus grace; no hard-deadline path leaves CPU-consuming work in the host.
- [ ] Preserve the published `evaluate_csharp` DTO, diagnostics, progress, caller cancellation, and concurrency/capacity semantics.
- [ ] Bound startup, IPC, output, and teardown; operational logs expose neither source nor environment detail.
- [ ] One non-cooperative-script regression observes the hard-deadline response, proves the child exits and capacity recovers, then completes a successful follow-up without restarting the host.

## Evidence

The former tests left eight infinite script workers alive in testhost. Their finite rewrite removes suite contamination, but production still cannot preempt arbitrary non-cooperative in-process C# after returning its hard-deadline result.

## Compatibility

Keep the public wire contract and documented timeout semantics stable. Moving execution behind an internal process boundary does not itself require a breaking public API.

## CI Evidence

In the 2026-08-24 class-parallel gate, the controlled one-second hard-deadline response did not complete within a three-second guard, while isolated repeated runs passed. Both the deadline callback and asynchronous finalization use the ThreadPool, making contention the leading mechanism; no scheduler trace was captured. The owned-process design must keep termination supervision outside the script child and must not rely exclusively on ThreadPool callbacks; retain a saturation regression for deadline response and child termination.
