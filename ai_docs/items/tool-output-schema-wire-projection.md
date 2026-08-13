# tool-output-schema-wire-projection — Project registered output schemas into tools/list

**row:** `tool-output-schema-wire-projection` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ToolOutputSchemaIndex.cs` (schema source — reflects `OutputSchemaTypeRef` → JSON Schema)
- `src/RoslynMcp.Host.Stdio/Program.cs` (startup pass writing each registered tool's `ProtocolTool.OutputSchema`)
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallContentProjector.cs` (consistency check only — runtime emission already works)
- `tests/RoslynMcp.Tests/` (SurfaceCatalogTests + catalog snapshot re-baseline)

## Acceptance

- [ ] `tools/list` publishes `outputSchema` for exactly the `OutputSchemaTypeRef` adopters (8 today: server_info, server_heartbeat, workspace_drift_check, workspace_list, workspace_status, workspace_health, workspace_readiness_report, workspace_support_bundle) and auto-covers future adopters
- [ ] Both channels still emitted per MCP spec (structuredContent + serialized content[].text)
- [ ] Catalog snapshot / surface tests updated; wire probe shows toolsWithOutputSchema == adopter count

## Evidence

- 8 tools emit runtime structuredContent but wire probe shows `toolsWithOutputSchema: 0` — schemas are published only via the catalog resource because all tools return `Task<string>` (SDK generates none) and nothing projects the index into the protocol objects. Projection gap, not adoption gap — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §5
