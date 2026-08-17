# mcp-sampling-mrtr-migration — Replace legacy client sampling

**row:** `mcp-sampling-mrtr-migration` · **pri:** `High` · **size:** `M` · **deps:** `mcp-mrtr-dispatch-contract,public-exception-detail-policy,mcp-logging-stderr-otel-migration,scaffolding-io-warning-detail-redaction`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`
- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `src/RoslynMcp.Core/Models/ScaffoldingDtos.cs` — sampling request/result DTO surface.
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] `scaffold_test_preview` obtains optional name suggestions through a request-scoped MRTR sampling `InputRequest` and consumes the corresponding `InputResponses` value.
- [ ] Deterministic placeholder behavior remains the default and requires no external credential.
- [ ] Unsupported clients receive the deterministic placeholder without a nested legacy sampling request.
- [ ] A provider exception carrying a secret sentinel yields a stable client fallback and sanitized server diagnostic structure; neither boundary contains the sentinel, raw message, or path.
- [ ] Preserve both existing cancellation invariants: `OperationCanceledException` from provider/request dispatch and response/retry handling propagates unchanged; include both in the same provider-outcome matrix and never convert either to deterministic fallback.
- [ ] The legacy MCP9005 Sampling suppression is removed from `ScaffoldingTools.cs`.

## Evidence

- ModelContextProtocol 2.1 deprecates legacy Sampling; MRTR can carry a request-scoped sampling input while the shared row owns dispatch mechanics.
- The deprecated provider currently returns `ex.GetType().Name` and `ex.Message` to the MCP client; this row owns that sampling-specific disclosure while the shared error row defines the public/internal detail policy.
