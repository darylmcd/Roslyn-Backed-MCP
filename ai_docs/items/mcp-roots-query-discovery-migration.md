# mcp-roots-query-discovery-migration — Remove Roots from query-anchored discovery

**row:** `mcp-roots-query-discovery-migration` · **pri:** `Medium` · **size:** `M` · **deps:** `mcp-roots-configured-validation-migration`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/SolutionDiscoveryHelper.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`
- `tests/RoslynMcp.Tests/SolutionDiscoveryHelperTests.cs`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterAutoLoadTests.cs`

## Acceptance

- [ ] Query-anchored solution discovery uses explicit configured/search roots or a request-scoped MRTR input instead of `roots/list`.
- [ ] File-anchored discovery remains unchanged.
- [ ] Zero, unique, and ambiguous candidate behavior stays deterministic and bounded.
- [ ] The legacy MCP9005 Roots suppression is removed from `SolutionDiscoveryHelper.cs`.

## Dependencies

- `mcp-roots-configured-validation-migration`

## Evidence

- ModelContextProtocol 2.1 deprecates `roots/list`; modern protocol clients use per-request input or explicit server/tool configuration.
**deps:** mcp-roots-configured-validation-migration
