# host-assembly-marker-foundation — Introduce stable Host assembly identity

**row:** `host-assembly-marker-foundation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- New `src/RoslynMcp.Host.Stdio/HostAssemblyMarker.cs`.
- `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs`
- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs`

## Acceptance

- [ ] Add one inert, documented Host assembly marker with no logging, transport, DI, or startup responsibility.
- [ ] Replace every Program/discovery/startup use of `typeof(McpLoggingProvider).Assembly` with the stable marker.
- [ ] Assembly version, server metadata, surface discovery, and startup diagnostic captures remain byte-equivalent.
- [ ] One table-driven marker invariant proves every anchored consumer resolves the same Host assembly before protocol logging is removed.

## Evidence

- Program and the two anchored suites use a deprecated logging provider as their unrelated Host assembly-identity token.
- Removing the bridge without first assigning stable ownership would create compile failures or encourage another incidental type dependency.
