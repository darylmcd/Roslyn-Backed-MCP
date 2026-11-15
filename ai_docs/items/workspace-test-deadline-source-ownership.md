# workspace-test-deadline-source-ownership — Dispose workspace test deadline sources

**row:** `workspace-test-deadline-source-ownership` · **pri:** `Low` · **size:** `S` · **verified:** `d9e2b3ffd8ab69e6bc20b0b06fe4fe7d57d63dd2`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceLoadFinalizerTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadCleanupGateReleaseTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadLinkRefusalTests.cs`

## Acceptance

- [ ] Bind the deadline source to the owning operation or fixture and dispose it on every exit. Preserve bounded waits and cancellation coverage in all three classes.

## Evidence

- All three test helpers allocate a timed CancellationTokenSource and return only its token. The test cannot dispose the source or release its timer promptly. Source sweep during workspace path guard review.
