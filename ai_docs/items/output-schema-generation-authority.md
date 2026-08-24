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
2026-08-20 adjacent review: JsonDefaults now includes custom public-projection converters and ToolOutputSchemaIndex clones those serializer options. .NET 10 JsonSchemaExporter can emit boolean true for a custom-converted type, erasing its DTO shape. Extend this row's fixed-versus-union matrix with BuildResultDto/TestRunResultDto projection parity or separate schema-export options before either type gains an advertised structured schema.
2026-08-24 SDK 2.2 servicing review: the SDK-generated output-schema behavior above is unchanged; treat 2.2.0 as the current pin for implementation and regression evidence.
