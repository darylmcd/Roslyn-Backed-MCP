# validation-test-compile-stub-deduplication — Share cancellation-only compile test stub

**row:** `validation-test-compile-stub-deduplication` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceValidationTimeoutTests.cs:209`
- `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs:636`

## Acceptance

- [ ] Both validation test fixtures use one shared cancellation-only `ICompileCheckService` stub with the same observable cancellation behavior.
- [ ] Remove duplicate private stub classes and keep timeout and recent-Git-change assertions intact.
- [ ] Focused tests for both fixtures pass.

## Evidence

- The two test classes keep separate copies of `SlowCompileCheckService`, including an explicit copy comment in `WorkspaceValidationTimeoutTests`. This is test helper duplication, separate from the wave-40 parallelization audit.
