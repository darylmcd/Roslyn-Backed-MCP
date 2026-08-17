# mcp-roots-fixture-lifecycle-consolidation — Reuse the owned in-memory MCP lifecycle

**row:** `mcp-roots-fixture-lifecycle-consolidation` · **pri:** `Medium` · **size:** `S` · **deps:** `test-assembly-cleanup-failure-observability,tool-call-error-envelope-wire-contract`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/McpRootsTestServerFactory.cs`
- `tests/RoslynMcp.Tests/Helpers/InMemoryMcpClientServerHarness.cs`
- `tests/RoslynMcp.Tests/Helpers/InMemoryMcpClientServerHarnessTests.cs`

## Acceptance

- [ ] Add a host/service-composition seam to the shared in-memory MCP harness and migrate the roots fixture to it.
- [ ] Preserve server-first teardown when a legacy client-Roots request may be in flight, while keeping deterministic client/server/stream/provider disposal and aggregated failures.
- [ ] Dispose every partially-created host, client, pipe stream, and provider when host start or client initialization fails.
- [ ] Add one forced roots-fixture initialization-failure regression proving all owned probes are disposed and the original exception remains observable.

## Evidence

- The roots migration introduced a second pipe/client/server lifecycle immediately after the original duplicate harnesses were consolidated. Unlike the shared helper, its client-initialization failure path does not dispose the started host or pipe streams.
