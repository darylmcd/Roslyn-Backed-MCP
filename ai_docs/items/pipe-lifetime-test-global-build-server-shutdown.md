# pipe-lifetime-test-global-build-server-shutdown — Isolate build-server cleanup in pipe-lifetime tests

**row:** `pipe-lifetime-test-global-build-server-shutdown` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/DotnetCommandRunnerPipeLifetimeTests.cs:14-24`
- `tests/RoslynMcp.Tests/DotnetCommandRunnerPipeLifetimeTests.cs:30-36`
- `tests/RoslynMcp.Tests/DotnetCommandRunnerPipeLifetimeTests.cs:164-193`

## Acceptance

- [ ] Setup and cleanup preserve unrelated build-server workers running under the same user.
- [ ] Use owned-process cleanup or an explicit isolated-host/exclusive-run guard for the pipe-descendant scenario; retain its regression coverage.
- [ ] Validate with a separately owned worker or concurrent build that survives the fixture's setup and cleanup.
- [ ] Keep the fixture serialized within its own test assembly and leave user configuration unchanged.

## Evidence

- ClassInit and ClassCleanup call ShutdownBuildServersAsync, which executes dotnet build-server shutdown.
- The source comment explicitly documents machine-wide cleanup. DoNotParallelize coordinates only the current test assembly; it does not protect other sessions or services.
- Found during the completed normal-user Release verification of gated-validate-test-phase-budget (3559 passed, 12 skipped). No impact on unrelated workers was measured; the cleanup mechanism is the source-level finding.
- Existing timing-test-reaudit-wave covers wall-clock sensitivity, not cleanup ownership.

## Context

- Separate validation-harness mechanism; do not change it as part of the validation test-budget fix.
