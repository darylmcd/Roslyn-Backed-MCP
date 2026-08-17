# host-assembly-marker-wire-test-migration — Finish Host marker test migration

**row:** `host-assembly-marker-wire-test-migration` · **pri:** `High` · **size:** `S` · **deps:** `host-assembly-marker-foundation`

## Anchors

- `tests/RoslynMcp.Tests/StructuredContentWireContractTests.cs`
- `tests/RoslynMcp.Tests/ToolDiResolutionTests.cs`

## Acceptance

- [ ] Replace the remaining test-only `McpLoggingProvider` assembly markers with `HostAssemblyMarker`.
- [ ] Preserve tool discovery, raw structured-content registration, and DI-resolution behavior exactly.
- [ ] Both suites compile and pass after `McpLoggingProvider` is removed; no test uses another incidental feature type as an assembly marker.

## Evidence

- These two suites reference the deprecated provider only to locate the Host assembly and would otherwise block bridge deletion.
- They are split from the foundation row to keep each backlog item within the three-test-file planning limit.
