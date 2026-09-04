# scripting-supervisor-outer-cancellation-contended-timeout — scripting-supervisor-outer-cancellation-contended-timeout

**row:** `scripting-supervisor-outer-cancellation-contended-timeout` · **pri:** `Medium` · **size:** `S`

# scripting-supervisor-outer-cancellation-contended-timeout — Make cancellation regression causal

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs`
- `tests/RoslynMcp.Tests/ScriptingServiceTests.cs`

## Acceptance

- Expose a test-owned causal seam that signals monitoring has started and allows cancellation completion to be controlled without production sleeps.
- Drive outer cancellation while a controlled script worker remains held and prove the result is `OuterCancelled`.
- After releasing the worker, prove active and abandoned execution counts return to zero without a fixed five-second `WaitAsync` timing dependency.
- Repeat the controlled contention shape enough times to distinguish a lifecycle defect from scheduler load.

## Regression

Hold one supervised worker behind the causal seam, cancel its caller after monitoring starts, assert `OuterCancelled`, release the worker, and observe both execution counters reach zero deterministically across repeated runs.

## Evidence

The 2026-09-04 full release gate failed `ScriptingServiceTests.OuterCancellation_WhileScriptStillRunning_ReturnsOuterCancelledAndAbandonsSlot`, while the exact test passed three consecutive isolated runs. The current regression couples correctness to a five-second wall-clock timeout under suite contention.
Anchor overlap with cancellation-invariant-regression-locks is intentional and accepted: that row covers internal-budget OCE classification plus a separate WorkspaceForkApply timeout path; this row covers the distinct outer-caller-cancellation lifecycle and removes a wall-clock-dependent contention test. Keep the regression shapes separate.
