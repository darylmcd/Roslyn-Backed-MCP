# pwsh-script-runner-launch-configuration-deduplication — Share test process launch configuration

**row:** `pwsh-script-runner-launch-configuration-deduplication` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs` — RunAsync and RunExecutableAsync duplicate timeout validation, ProcessStartInfo settings, and argument population.
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunnerTests.cs` — argument, exit, and process ownership coverage.

## Acceptance

- Share one internal launch-configuration helper while retaining the PowerShell executable default and optional environment overrides.
- Both entry points reject nonpositive timeouts consistently and preserve argument boundaries, redirected streams, and hidden-window behavior.
- Exercise both entry points through the shared argument-boundary regression shape; preserve cancellation and drain tests.

## Evidence

2026-09-15 direct review: RunAsync and RunExecutableAsync independently construct the same redirected, noninteractive ProcessStartInfo and populate ArgumentList; configuration fixes currently require duplicate edits.
