# formatter-baseline-nested-child-handshake-flake — Stabilize the nested-child handshake wait under full-suite load

**row:** `formatter-baseline-nested-child-handshake-flake` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs`

## Acceptance

- [ ] `TimedOutCapture_RetainsPhaseMarkerAndTerminatesRecordedNestedChildAsync` no longer fails when a nested `pwsh` cold start exceeds 10 s under full-suite parallel load: either widen both handshake deadlines (outer script and test wait) to a contention-safe bound, or serialize the class with a source-adjacent `[DoNotParallelize]` rationale naming the child-process start dependency.
- [ ] Assertions on phase-marker retention and nested-child termination stay unchanged; no sleeps are added.
- [ ] Repeated full-suite runs (≥3) plus repeated isolated runs stay green.

## Evidence

- Failed once in the `/backlog-remediate` chain gate for `donotparallelize-audit-wave-13` (plan `20260924T025012Z_backlog-remediate`, full `just ci`, 1 failed / 3147 passed): `The nested child did not publish its handshake.`
- Construct at HEAD, `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs:426`: `Assert.IsTrue(await WaitForFileAsync(childPidPath, TimeSpan.FromSeconds(10)), "The nested child did not publish its handshake.");` The outer fixture script uses the same 10 s deadline (`$deadline = [DateTime]::UtcNow.AddSeconds(10)`).
- Non-deterministic: the class passed 3/3 isolated runs (10/10 each) immediately after, and the per-PR gate for the same PR passed on retry-free re-run.
- The DoNotParallelize audit waves raise parallel-phase load, which widens process-start latency for this parallel-phase, process-spawning class.

## Context

Added by PR #1582 (`formatter-baseline-contended-nested-process-timeout-investigation`). Related: `pwsh-script-runner-launch-configuration-deduplication` (shared launch/timeout plumbing).

## Second occurrence — 2026-09-24 (plan `20260924T162035Z_backlog-remediate`)

- The strict per-PR integration gate for PR #1612 (an unrelated path-validator message change) failed `FormatterBaselineContractTests.TimedOutCapture_RetainsPhaseMarkerAndTerminatesRecordedNestedChildAsync` with `The nested child did not publish its handshake` at the 10 s `WaitForFileAsync(childPidPath, ...)`; the full suite took 39 min under cross-repo CPU contention. The re-run under a quiet machine passed.
- Two occurrences in two consecutive plans, each blocking a strict per-PR landing until re-run. Consider raising priority; the 10 s handshake budget is the load-sensitive part.
