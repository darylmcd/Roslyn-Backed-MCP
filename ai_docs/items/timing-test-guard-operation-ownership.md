# timing-test-guard-operation-ownership — Settle timed-out correctness checks

**row:** `timing-test-guard-operation-ownership` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs:451`
- `tests/RoslynMcp.Tests/ValidationToolsIntegrationTests.cs:391`
- (new) `tests/RoslynMcp.Tests/Helpers/TimeBoundTestOperation.cs`

## Acceptance

- [ ] Both 60-second hang guards cancel their underlying operations and settle before fixture cleanup, with a bounded fallback if cancellation is ignored.
- [ ] Preserve the primary timeout and attach cancellation/settlement failures as additional evidence.
- [ ] Red-first regressions show an old timed-out waiter leaves work active and a secondary settlement fault displaces timeout; passing tests prove both repairs.

## Evidence

- Review of merged PR #1969 found `WaitAsync(60s)` around operations passed `CancellationToken.None`, so a timed-out waiter could release LoadLock or tear down a shared fixture while the operation continued. Follow-up implementation review caught a settlement fault replacing the primary timeout.
