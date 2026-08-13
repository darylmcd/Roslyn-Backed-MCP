# mcp-sampling-mrtr-migration — Replace legacy client sampling

**row:** `mcp-sampling-mrtr-migration` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`
- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `src/RoslynMcp.Core/Models/ScaffoldTestDto.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] `scaffold_test_preview` obtains optional name suggestions through request-scoped MRTR input, or removes the experimental option through the public deprecation policy.
- [ ] Deterministic placeholder behavior remains the default and requires no external credential.
- [ ] Cancellation and unsupported-client behavior are covered without swallowing failures.
- [ ] The legacy MCP9005 Sampling suppression is removed from `ScaffoldingTools.cs`.

## Evidence

- ModelContextProtocol 2.1 deprecates legacy Sampling; MRTR is the SDK-supported request-scoped path for protocol 2026-07-28.
2026-08-13 adjacent security review: the deprecated provider currently returns ex.GetType().Name and ex.Message to the MCP client. The migration must replace that with a stable sanitized fallback message while retaining exception detail only in server-side diagnostics, and add a regression using a sensitive sentinel message.
