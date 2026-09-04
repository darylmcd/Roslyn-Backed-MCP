# formatter-baseline-contended-nested-process-timeout-investigation — Identify the contended child-process stall

**row:** `formatter-baseline-contended-nested-process-timeout-investigation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs`
- `eng/generate-format-baseline.ps1`

## Acceptance

- Add bounded phase and owned-child identity evidence that distinguishes restore, build, formatter invocation, and harness wait without logging command arguments, source, or environment values.
- Reproduce the timeout with a deterministic contention seam or a bounded stress harness before changing production behavior.
- Fix the proven stall within these anchors, or split a cause-specific implementation row if source verification requires more production files.
- One regression runs the proven contention shape to completion within a mechanically derived bound and leaves no owned child process behind.

## Regression

Drive the smallest causal contention shape found by the investigation, assert the generator returns its intended check result within the derived bound, and verify its owned process tree is empty afterward.

## Evidence

During PR #1473 validation, the full suite timed out the formatter baseline contract after five minutes with 48 competing `dotnet` processes at start and 28 still present. An isolated attempt under the same contention also timed out, while direct `-Check` and `-Check -NoRestore` invocations completed in 32–39 seconds; the exact test later passed in 3 minutes 58 seconds. Cold review confirmed the harness already starts both `ReadToEndAsync` drains before waiting and already kills plus drains the owned tree on timeout, so the present evidence does not establish a pipe-buffer deadlock.
