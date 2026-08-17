# output-schema-generation-authority — Establish one output-schema generation authority

**row:** `output-schema-generation-authority` · **pri:** `Medium` · **size:** `M` · **deps:** `host-assembly-marker-foundation`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ToolOutputSchemaIndex.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/SurfaceRegistrationPolicy.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`
- `tests/RoslynMcp.Tests/Batch1OutputSchemaTests.cs`
- `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs`

## Acceptance

- [ ] Document and enforce one generation authority for every advertised fixed DTO schema.
- [ ] Prove custom catalog schemas use the same serializer metadata/options as SDK runtime output before replacing an SDK-generated schema.
- [ ] Keep the intentional `workspace_list`/`workspace_status` union contracts explicit and schema-valid without blind overwrite.
- [ ] One fixed-versus-union matrix compares catalog, SDK-discovered, and raw advertised schemas.

## Evidence

- SDK 2.1 generates a schema from `McpServerToolAttribute.OutputSchemaType`, then `SurfaceRegistrationPolicy` overwrites it from `ToolOutputSchemaIndex`; fixed schemas can drift even though variant unions require a deliberate custom contract.
