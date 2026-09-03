# test-assembly-fixture-disposal-observable-postcondition — Prove the fixture release block ran

**row:** `test-assembly-fixture-disposal-observable-postcondition` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestAssemblyFixtureTests.cs`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestAssemblyFixture.cs`
- `tests/RoslynMcp.Tests/WorkspaceIdCache.cs`

## Acceptance

- [ ] `DisposeAsync_ReleasesOwnedResourcesExactlyOnce` asserts an observable post-condition proving the release block RAN, not merely that it was entered — deleting the block's body must fail the test.
- [ ] The assertion is deterministic: it passes and fails identically in isolation and under the full suite, with no dependence on state other tests accumulate.
- [ ] `WorkspaceIdCache` exposes whatever minimal read surface the assertion needs (e.g. a count or contains check), used only by the regression.

## Evidence

`TestAssemblyFixture.DisposeAsync` increments its entry counter BEFORE the
`CleanupFailureCollector` block, so `DisposalEntryCount` proves idempotence but would stay green if
the block's body were deleted. Raised as a `low` untested-critical-path finding by the cold review of
PR #1431.

An observable post-condition WAS attempted in that PR and reverted, with the failure recorded here so
it is not retried blindly: asserting that the disposed `WorkspaceManager` throws
`ObjectDisposedException` from `LoadAsync` passed in isolation (4/4) and FAILED under the full gate
(2840 passed / 1 failed). `LoadAsync` throws only once it reaches the semaphore disposed by
`WorkspaceManager.Dispose`; with the sample solutions pre-prepared during a full run it
short-circuits earlier and returns without throwing. The assertion was therefore dependent on state
accumulated by the rest of the suite.

## Context

The obvious deterministic check is that `WorkspaceIdCache.Clear()` ran, but `WorkspaceIdCache` today
exposes only `GetOrLoadAsync` and `Clear` — no read surface — so the assertion needs a small accessor
on `tests/RoslynMcp.Tests/WorkspaceIdCache.cs`. That is a third test file beyond the two PR #1431
already owned, which would have breached its Rule 4 budget of 3. Hence a separate row rather than an
in-place fix.

Referenced by a comment in `TestAssemblyFixtureTests.DisposeAsync_ReleasesOwnedResourcesExactlyOnce`.

[source: 2026-09-03 backlog-remediate PR #1431 cold review]
