# test-run-full-suite-timeout-envelope — test-run-full-suite-timeout-envelope

**row:** `test-run-full-suite-timeout-envelope` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `tests/RoslynMcp.Tests/ValidationToolsTests.cs`

## Acceptance

- [ ] Convert an unfiltered test-run timeout or runner cancellation into the documented structured failure envelope rather than an unclassified tool error.
- [ ] Keep filtered successful test runs and existing eviction retry behavior unchanged.
- [ ] Add a controlled runner-cancellation regression that asserts a stable timeout/error envelope.

## Evidence

- The live full-suite `test_run` held for about 120 seconds then returned `isError:true` without structured content; the focused nine-test path passed.
