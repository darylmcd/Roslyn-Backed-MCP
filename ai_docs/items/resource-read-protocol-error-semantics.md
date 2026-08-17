# resource-read-protocol-error-semantics — Return resource failures through the protocol error channel

**row:** `resource-read-protocol-error-semantics` · **pri:** `High` · **size:** `M` · **deps:** `tool-error-envelope-sensitive-detail-disclosure`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` — `ExecuteResource` / `ExecuteResourceAsync`.
- `src/RoslynMcp.Host.Stdio/Resources/WorkspaceResources.cs`
- `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/ResourceReadResultFilter.cs` — request-scoped read boundary and protocol selection.
- New `tests/RoslynMcp.Tests/ResourceReadWireContractTests.cs`.
- `tests/RoslynMcp.Tests/WorkspaceResourceTests.cs`

## Acceptance

- [ ] Unknown workspace/file and invalid range/pagination failures use the SDK resource/protocol error path and never return a successful `contents` array containing an error document.
- [ ] Move exception translation to the registered read-resource filter around `next(context, cancellationToken)`; remove resource wrappers that catch and serialize failures as content.
- [ ] Pin missing resources as `McpErrorCode.ResourceNotFound` (`-32002`) under 2025-11-25 and `McpErrorCode.InvalidParams` (`-32602`) under 2026-07-28; malformed range/pagination is `InvalidParams` in both eras, and unexpected failure is sanitized `InternalError` (`-32603`).
- [ ] Obtain the negotiated protocol from the SDK request context at that boundary and pass it explicitly to classification; never cache protocol state statically or per session.
- [ ] Use/lock `McpProtocolVersions.UseInvalidParamsForMissingResource(protocol)` as the version selector rather than duplicating protocol-string comparisons.
- [ ] Public errors carry stable sanitized category/remediation and source identity without exception type, stack, raw path, or secret-bearing detail.
- [ ] Successful JSON and text resources retain MIME type, content, metadata, and ordering; preserve the version-gated cache contract locked by `ServerDiscoveryWireTests`.
- [ ] One table-driven raw-wire matrix covers not-found, invalid-input, and unexpected-handler failures.
- [ ] Classify the success-result to protocol-error correction and add required consumer migration guidance.

## Evidence

- A raw read of `roslyn://workspace/does-not-exist/status` returned JSON-RPC success with one content body whose JSON said `error: true` and `category: NotFound`.
- The resource wrappers catch exceptions and return formatted JSON strings, preventing the SDK from using its failure channel.
