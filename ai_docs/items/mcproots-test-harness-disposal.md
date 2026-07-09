# mcproots-test-harness-disposal — Dispose McpServer and transport streams in ServerWithSanctionedRootHarness

**row:** `mcproots-test-harness-disposal` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:625-655`

## Acceptance

- [ ] `ServerWithSanctionedRootHarness.DisposeAsync` also awaits `Server.DisposeAsync()` and disposes the `clientToServer`/`serverToClient` Pipe stream wrappers
- [ ] Root-rejection tests still pass unchanged

## Evidence

- Harness leaves an `IAsyncDisposable` `McpServer` and duplex `Pipe` streams undisposed per test; managed-only so no OS-handle leak, but teardown does not model production disposal — see code-quality review, PR #1034.
