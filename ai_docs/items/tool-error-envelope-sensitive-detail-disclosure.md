# tool-error-envelope-sensitive-detail-disclosure — Sanitize public tool error envelopes

**row:** `tool-error-envelope-sensitive-detail-disclosure` · **pri:** `High` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`
- `tests/RoslynMcp.Tests/BacklogFixTests.cs`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs`

## Acceptance

- [ ] Unexpected exceptions return a stable client-safe category, remediation, and correlation reference without raw message, type, inner chain, stack frame, source path, or secret-bearing value.
- [ ] Server diagnostics retain correlation, exception type/inner shape, and stack structure without recursively using the MCP logging bridge or persisting secret-bearing message values.
- [ ] Expected user-correctable validation/not-found errors remain actionable but never echo raw `ex.Message`, full paths, or secret-bearing supplied values.
- [ ] Invert the tests that currently require exception type, inner messages, or `stackTrace` in client results.
- [ ] A nested secret-sentinel regression uses an enabled in-memory capture sink and proves absence from both the serialized client envelope and captured diagnostics while benign diagnostic structure and correlation remain observable.
- [ ] Classify the public envelope correction under `docs/release-policy.md`.

## Evidence

- `ToolErrorHandler` currently emits exception type/message, up to three inner messages, and up to five stack frames.
- Existing tests explicitly lock this client-visible detail as a diagnostic contract.
- Sampling and test-coverage domain fallbacks are owned by their dedicated rows because neither reaches this shared envelope boundary.
