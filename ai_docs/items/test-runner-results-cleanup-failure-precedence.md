# test-runner-results-cleanup-failure-precedence

**row:** `test-runner-results-cleanup-failure-precedence` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs` — RunTestsAsync results-directory finally block.
- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs` — controlled executor regressions.

## Acceptance

- [ ] Preserve an existing test result or primary cancellation/timeout when temporary-result cleanup fails.
- [ ] Report cleanup failure through the existing secret-safe diagnostic sink without leaking temporary paths.
- [ ] Add one injected cleanup-failure regression covering outcome precedence without platform-specific file locking.

## Evidence

2026-09-15 direct review: unguarded Directory.Delete in RunTestsAsync finally can replace the returned parsed result or propagating cancellation with an IOException or UnauthorizedAccessException.
