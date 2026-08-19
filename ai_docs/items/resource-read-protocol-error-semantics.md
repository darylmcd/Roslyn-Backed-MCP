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

## Amendment — part 1 shipped (PR #1282, sweep 20260819T180531Z)

Part 1 (`resource-read-protocol-error-semantics-workspace`) landed the `ResourceReadResultFilter` translation path for the WORKSPACE resource family. Two follow-ups belong to part 2 (`-server-catalog`), which already owns `ResourceReadWireContractTests.cs` and `ToolErrorHandler.cs` — do NOT file these as sibling rows, they would collide as PRs.

### Additional acceptance for part 2

- `ResourceReadWireContractTests` covers the WORKSPACE family too: `NotFound` -> `-32002` under 2025-11-25 and `-32602` under 2026-07-28; malformed `lineRange` -> `-32602` in both eras; an unexpected handler failure -> `-32603` with no exception type name, stack frame, or absolute server path in the payload.
- One case drives `source_file`'s `McpToolException` wrapper and asserts the INNER `KeyNotFoundException` drives classification (not the InternalError fallback), pinning the unwrap branch at `ResourceReadResultFilter.cs:69`.
- `MapErrorCode` consumes shared category constants declared next to `ToolErrorHandler`'s classification table (no duplicated string literals).
- `"InvalidOperation"` from a caller-supplied bad `filePath` resolves to `-32602`, not `-32603`.
- Either the caught exception is reported through the existing `UnexpectedExceptionReporting`/`ServerObservability` seam with the emitted correlation id, OR the `ExecuteResourceScope*` `finally { RecordElapsed }` and its "failed reads still stamp ElapsedMs" doc claim are removed as unobservable.
- Part 2 deletes the now-dead `ToolErrorHandler.ExecuteResourceAsync` (zero callers after part 1's migration).

### Additional anchors

- `src/RoslynMcp.Host.Stdio/Middleware/ResourceReadResultFilter.cs:63` — untested failure path
- `src/RoslynMcp.Host.Stdio/Middleware/ResourceReadResultFilter.cs:77` — `MapErrorCode` magic strings
- `src/RoslynMcp.Host.Stdio/Middleware/ResourceReadResultFilter.cs:90` — `BuildSanitizedMessage` correlation id with no server-side record
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:270` — unobservable `finally { RecordElapsed }`

### Evidence

`git diff --stat -- tests` for PR #1282 is net -27 lines across 3 files; grep for `McpProtocolException`/`ResourceNotFound` across `tests/` returns only comment lines. No test constructs the filter or asserts a JSON-RPC code, so the entire new translation + sanitization path shipped uncovered. Separately, `AmbientGateMetrics.Current` is an AsyncLocal read only by `Snapshot()` inside `InjectMetaIfPossible`, and the `using` scope in `ExecuteResourceScopeAsync` disposes before the exception reaches the filter — the finally-stamped `ElapsedMs` has no reader.

Source: code-quality review of PR #1282.
