# nuget-version-checker-test-wallclock-poll — replace wall-clock polling with a deterministic completion seam

**row:** `nuget-version-checker-test-wallclock-poll` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Services/NuGetVersionChecker.cs` (test seam)
- `tests/RoslynMcp.Tests/NuGetVersionCheckerTests.cs` (`WaitForCompletionAsync`)
- `tests/RoslynMcp.Tests/ServerInfoUpdateLatestTests.cs` (`WaitForTerminalStatusAsync`)

## Acceptance

- [ ] In-flight fetch `Task` (or a completion `TaskCompletionSource`) exposed as a test seam
- [ ] The tests await a deterministic completion signal, no wall-clock poll

## Evidence

- 2026-06-05 top-5 remediation code-quality review (row 1), expanded during `latest-version-status-surface` review.

## Context

Follow-on to `nuget-version-check-observability` (SHIPPED #939). The two wait helpers poll `LastCheckStatus` on 10s wall-clock deadlines with `Task.Delay(25)` rather than awaiting the background fetch deterministically — under heavy CI parallel load the fire-and-forget `Task.Run` could be starved and miss the deadline, producing a non-deterministic `Assert.Fail`. 10s is generous so flake risk is low, but the pattern is timing-dependent.
