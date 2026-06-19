# nuget-version-checker-timeout-test-wallclock-race — flaky timeout test under runner load

**row:** `nuget-version-checker-timeout-test-wallclock-race` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/NuGetVersionCheckerTests.cs:186` (`GetLatestVersion_OnTimeout_StaysNonThrowingAndRecordsTimedOutStatus`)
- `tests/RoslynMcp.Tests/NuGetVersionCheckerTests.cs:107-114` (`WaitForCompletionAsync` — the 5 s wall-clock `WaitAsync`)
- `tests/RoslynMcp.Tests/NuGetVersionCheckerTests.cs:96-102` (`TimeoutHandler` — `Task.Delay(Infinite, ct)` until the checker's internal timeout cancels)

## Acceptance

- [ ] `GetLatestVersion_OnTimeout_...` no longer throws `System.TimeoutException` under self-hosted runner load — the helper waits deterministically for the checker's `TimedOut` status (event-driven, or a bound comfortably larger than the checker's own internal timeout), not a fixed 5 s wall-clock `WaitAsync` that can expire before the checker records the status.
- [ ] No wall-clock-only bound in `WaitForCompletionAsync` that can fire before the background check completes.

## Evidence

- Flaked on PR #986 validate (2026-06-19): `NuGetVersionCheckerTests.GetLatestVersion_OnTimeout_StaysNonThrowingAndRecordsTimedOutStatus threw exception: System.TimeoutException: The operation has timed out.` 1560/1561 passed; re-ran green with zero code change. The change under test (a csproj packaging edit) cannot affect a NuGet-version-checker timeout test — confirmed transient.

## Context

`WaitForCompletionAsync` waits up to **5 s** (`pending.WaitAsync(TimeSpan.FromSeconds(5))`) for the checker's background fetch. `TimeoutHandler` blocks forever and relies on the checker's OWN internal bounded timeout to fire and record `TimedOut`. Under self-hosted runner load the 5 s helper bound expires before the checker's internal timeout records the status, so `WaitAsync` throws. **Recurrence** of the previously-shipped `nuget-version-checker-test-wallclock-poll` fix — that pass did not remove the wall-clock race. Same class as the `ci-flaky-fswatcher-staleness-test` fix (replace wall-clock with event-driven completion). Release-gating exposure: this runs inside `verify-release.ps1`, which gates the ubuntu publish path. NOT yet in `ai_docs/known-flakes.md` (prefer fixing over registering).
