# scaffold-sampling-mrtr-replay-cost — avoid repeating expensive scaffold preparation across MRTR replay

**row:** `scaffold-sampling-mrtr-replay-cost` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`
- `src/RoslynMcp.Core/Services/IScaffoldingService.cs`
- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `tests/RoslynMcp.Tests/SamplingMrtrWireTests.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] Separate sampling-context preparation from final scaffold preview so the MRTR retry does not repeat project resolution, compilation, or sibling-test discovery.
- [ ] Preserve stateless operation with bounded, opaque, secret-safe request state; do not cache client payloads or absolute paths in process-static state.
- [ ] A production-tool wire regression proves one expensive preparation, one client sampling request, and one completed preview across the input-required/retry exchange.

## Evidence

The production `scaffold_test_preview` MRTR regression correctly observes two service/provider entries because the protocol replays the entire tool call. `SingleTestScaffolder` derives the sampling context only after project resolution, compilation, and sibling inference, so those expensive steps currently repeat before the retry can consume the response.
