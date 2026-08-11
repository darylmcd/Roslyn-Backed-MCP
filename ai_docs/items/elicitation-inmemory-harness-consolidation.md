# elicitation-inmemory-harness-consolidation — extract a shared in-memory MCP client/server test harness

**row:** `elicitation-inmemory-harness-consolidation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs:378-478` (`CreateServerWithHangingElicitationAsync` / `ElicitationServerHarness`)
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:703-808` (`CreateServerWithSanctionedRootAsync` / `ServerWithSanctionedRootHarness`)

## Acceptance

- [ ] One shared helper (single file in `tests/RoslynMcp.Tests`) wires the `Pipe`/`StreamServerTransport`/`StreamClientTransport` pair and the `IAsyncDisposable` harness, parameterized by `ClientCapabilities` + `McpClientHandlers`.
- [ ] Both existing call sites use it; the duplicated `DisposeAsync`/`DisposeCapturingAsync` bodies exist exactly once.

## Evidence

Traced during code-quality review of `elicitation-trychoice-cancellation-swallow`: read both bodies side by side — the pipe wiring, the four captured streams, the client-then-cts-then-serverRunTask dispose order, the `OperationCanceledException` swallow, and `DisposeCapturingAsync` are character-for-character identical between the two harnesses; only the transport name literal and the capability/handler object differ.

## Context

Spin-off from the `elicitation-trychoice-cancellation-swallow` row's code-quality review (top-n-remediation run 20260810T233007Z).
