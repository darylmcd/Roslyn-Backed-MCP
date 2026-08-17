# prompt-call-error-filter-boundary — Add a prompt-call error boundary

**row:** `prompt-call-error-filter-boundary` · **pri:** `High` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs` — prompt handler/filter registration.
- New focused get-prompt error filter under `src/RoslynMcp.Host.Stdio/Middleware/`.
- `src/RoslynMcp.Host.Stdio/Prompts/PromptMessageBuilder.cs` — current successful user-role error construction.
- New `tests/RoslynMcp.Tests/PromptCallErrorFilterTests.cs` — focused filter/wire contract.

## Acceptance

- [ ] Register one boundary through `requestFilters.AddGetPromptFilter(...)`; it owns unexpected get-prompt logging, public sanitization, and protocol error propagation.
- [ ] A nested secret-sentinel exception produces a sanitized `McpProtocolException`/`InternalError` (`-32603`), not a successful prompt message; invalid parameters retain the SDK's `InvalidParams` contract.
- [ ] The serialized failure contains no raw message, exception type, inner chain, stack, or local path; server diagnostics retain correlation/type/stack structure but not secret-bearing message values.
- [ ] Cancellation propagates unchanged.
- [ ] Expected parameter-validation failures remain actionable without leaking supplied secret-bearing values.
- [ ] Record the success-to-error channel correction in the public compatibility decision and migration note.

## Evidence

- All current prompt handlers catch non-cancellation exceptions themselves, preventing a common protocol boundary from observing failures.
- `PromptMessageBuilder.CreateErrorMessage` places raw `ex.Message` in a successful user-role prompt.
