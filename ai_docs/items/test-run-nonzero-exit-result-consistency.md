# test-run-nonzero-exit-result-consistency — Keep nonzero test exits internally consistent

**row:** `test-run-nonzero-exit-result-consistency` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs`
- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs`

## Acceptance

- [ ] A nonzero process exit can never project a clean-success result, even when partial output contains passing test counts.
- [ ] Preserve parsed counts as diagnostics while attaching the correct build, test, or unknown failure envelope.
- [ ] One deterministic mixed-output fixture covers nonzero exit, partial passing counts, and failure classification.

## Evidence

- A build-failing focused run reported exit code 1 with `total=1`, `passed=1`, and no failure envelope.
