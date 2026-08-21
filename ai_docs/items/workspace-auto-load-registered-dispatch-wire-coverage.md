# workspace-auto-load-registered-dispatch-wire-coverage — cover successful auto-load through registered tools

**row:** `workspace-auto-load-registered-dispatch-wire-coverage` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterAutoLoadTests.cs`
- `tests/RoslynMcp.Tests/WorkspacePathMrtrWireTests.cs`

## Acceptance

- [ ] Drive `StructuredCallToolFilter.Create` over a live in-memory MCP dispatcher with zero loaded workspaces and one discoverable sanctioned solution.
- [ ] Prove the filter invokes the registered `workspace_load` primitive, patches its returned id, and dispatches the original tool once with `_meta.autoResolution=auto-loaded` and bounded load timing.
- [ ] Prove discovery/load cancellation and a load result without an id do not mutate arguments or dispatch the original tool.

## Evidence

The focused auto-load suite previously claimed end-to-end coverage but only exercised pure helpers and envelope projection. Path recovery now proves the shared registered-tool primitive, but the unique-discovery auto-load control-flow branch itself has no live success regression.
