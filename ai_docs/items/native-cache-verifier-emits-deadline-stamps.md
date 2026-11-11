# native-cache-verifier-emits-deadline-stamps — Compare the verifier's own start/deadline stamps instead of launch-relative elapsed time

**row:** `native-cache-verifier-emits-deadline-stamps` · **pri:** `Low` · **size:** `S` · **verified:** `95f651345b37a2849fe02bd026f827813658573f`

## Anchors

- `tests/RoslynMcp.Tests/Fixtures/pwsh-startup-cache-verify.ps1:7` — `$deadline` computed here; no timestamps emitted.
- `tests/RoslynMcp.Tests/LinuxPowerShellStartupCacheOwnershipTests.cs:149-170` — elapsed measured from a Stopwatch taken before launch.

## Acceptance

- [ ] The script prints its start and deadline UTC stamps (machine-readable lines) and the test asserts the failure was reported at or after the script's own deadline, not a launch-relative bound.
- [ ] Red first: a verifier variant that fails at startup plus half the budget fails the test (the current launch-relative assertion passes it whenever pwsh startup exceeds half the budget).

## Evidence

- PR #2065 comment at LinuxPowerShellStartupCacheOwnershipTests.cs:147-150: the reference precedes the script deadline "regardless of pwsh startup latency", so the bound is loose by that latency.
