# test-coverage-unexpected-error-detail-redaction — Sanitize test-coverage unexpected failures

**row:** `test-coverage-unexpected-error-detail-redaction` · **pri:** `High` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs`
- `src/RoslynMcp.Roslyn/Services/TestCoverageCoordinator.cs`
- `tests/RoslynMcp.Tests/TestCoverageFailureEnvelopeTests.cs`

## Acceptance

- [ ] Unexpected exceptions produce a stable sanitized coverage-failure category/summary without raw message, type, stack, path, or secret sentinel.
- [ ] The server-side logger retains correlation and nested exception structure through the non-wire observability boundary without persisting the secret sentinel or raw user-derived message.
- [ ] Expected coverage-domain failures, cancellation, timeout, and partial-result semantics remain unchanged.
- [ ] One table-driven failure DTO test distinguishes actionable domain text from an unexpected secret-bearing exception.
- [ ] Classify the public DTO correction and add migration guidance if required.

## Evidence

- `TestCoverageTools` catches unexpected exceptions before `ToolErrorHandler` and builds a successful domain failure DTO from `ex.Message`.
- Existing failure-envelope tests intentionally require an actionable domain message, so this path cannot be folded into the generic tool-envelope policy without preserving that contract.
