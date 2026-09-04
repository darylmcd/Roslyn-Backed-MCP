# mcp002-synthetic-test-tool-noise — Remove synthetic tool analyzer noise

**row:** `mcp002-synthetic-test-tool-noise` · **pri:** `Low` · **size:** `S` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/ToolCallErrorWireContractTests.cs:339`
- `analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.cs`

## Acceptance

- [ ] Decide whether MCP002 should ignore nested synthetic test tool types or the fixture should use the supported description shape.
- [ ] Preserve the intentional unclassified-exception wire contract and tool discovery behavior.
- [ ] Make solution-level semantic compile checks return no MCP002 diagnostic for the fixture.

## Evidence

- Current-session `compile_check` reported MCP002 on `SyntheticUnexpectedFailureTools.Fail`; its XML documentation explains the deliberate exception shape rather than user-facing tool metadata.
