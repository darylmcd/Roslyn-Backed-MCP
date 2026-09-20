# pwsh-script-runner-launch-configuration-deduplication — Share test process launch configuration

**row:** `pwsh-script-runner-launch-configuration-deduplication` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs` — duplicated launch configuration and current shared ownership boundary.
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunnerTests.cs` — argument, exit, cancellation, and process-ownership coverage.
- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs` — incremental phase/PID capture and bounded owned-tree teardown consumer.

## Acceptance

- [ ] One shared launch and owned-tree teardown path supports bounded incremental stdout/stderr snapshots plus complete exit/drain capture.
- [ ] PowerShell and generic executable entry points retain defaults, optional environment overrides, argument boundaries, hidden-window behavior, and consistent nonpositive-timeout rejection.
- [ ] The formatter-baseline fixture uses the shared path while preserving PID, phase, classification, cancellation, cleanup-budget, and drain diagnostics.
- [ ] Shared regressions cover early assertion failure and prove no owned outer or descendant process remains after return.

## Evidence

2026-09-15 direct review: RunAsync and RunExecutableAsync independently construct the same redirected, noninteractive ProcessStartInfo and populate ArgumentList; configuration fixes currently require duplicate edits.
