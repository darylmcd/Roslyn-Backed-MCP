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

## Amendment — 2026-09-02 (cold plan-deepener; verified against live source, no code shipped)

Row is **shovel-ready**: all five anchors resolve, dep `host-assembly-marker-foundation` verified SHIPPED (`src/RoslynMcp.Host.Stdio/HostAssemblyMarker.cs`, consumed at `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs:204`) — not merely absent from the backlog.

**Root cause.** `src/RoslynMcp.Host.Stdio/Catalog/SurfaceRegistrationPolicy.cs:54-56` unconditionally replaces the SDK-generated `tool.ProtocolTool.OutputSchema` for every tool the catalog index knows, with no comparison against what it clobbers. Only 2 of the 8 adopters need a custom contract — `ToolOutputSchemaIndex.cs:106-122` hand-builds `anyOf` for `workspace_list` and `oneOf` for `workspace_status`; the other 6 fall through `_ => GenerateSchema(schemaType)` and silently duplicate SDK work.

**The overwrite is load-bearing, not wrong.** `McpServerOptions.SerializerOptions` is never assigned anywhere in the repo, so the SDK generator runs on its own defaults; the catalog exporter clones `JsonDefaults.Indented` (`ToolOutputSchemaIndex.cs:38-46`) and runtime `structuredContent` uses that same options object (`StructuredToolResult.cs:27` — camelCase + `JsonStringEnumConverter` + 2 DTO converters at `JsonDefaults.cs:9-19`). The catalog is the correct authority; the defect is that this is nowhere declared or asserted, so a fixed schema can drift with no gate.

**Approach.** Declare `ToolOutputSchemaIndex` the single authority (explicit `Fixed` vs `Union` per-tool declaration replacing the implicit switch), and make `SurfaceRegistrationPolicy` overwrite only from that declaration and fail closed on either asymmetry (SDK schema with no catalog declaration, or vice versa) — mirroring the existing guard style at `SurfaceRegistrationPolicy.cs:37-42`.

**Scope (fits base Rule 3, no exemption):** production 2 — `src/RoslynMcp.Host.Stdio/Catalog/ToolOutputSchemaIndex.cs`, `src/RoslynMcp.Host.Stdio/Catalog/SurfaceRegistrationPolicy.cs`. Tests 2 extended — `tests/RoslynMcp.Tests/Batch1OutputSchemaTests.cs` (the fixed-versus-union matrix), `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs` (`:206` upgraded from name-set to content equivalence). `ServerSurfaceCatalog.cs` (addenda HOTSPOT) is deliberately NOT edited — it only consumes `GetSchema(name)` at `:437` and that signature is preserved. Fanout probe: 3 production consumers, 9 test consumers, all signature-preserving.

**Constraint for the executor.** The advertised schema for the 6 fixed adopters must stay byte-identical — this initiative *proves* the existing authority, it does not switch it. Letting the SDK win drops camelCase + the enum converter and breaks `StructuredContentWireContractTests.cs:61-69`.

**Deferred, not solved:** the 2026-08-20 `.NET 10 JsonSchemaExporter` boolean-`true` risk applies to `BuildResultDto`/`TestRunResultDto`, neither of which is an advertised adopter today; the new fail-closed guard surfaces it the moment either gains a schema.
