# workspace-validation-error-detail-redaction — Sanitize workspace-validation fallback detail

**row:** `workspace-validation-error-detail-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`
- `tests/RoslynMcp.Tests/ValidationIntegrationTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceValidationOverallStatusTests.cs`

## Acceptance

- [ ] Dotnet-test and git configuration/start/status failures retain stable operation/category/recovery hints without raw exception message, path, or secret sentinel.
- [ ] Server diagnostics use the shared secret-safe diagnostic projection and request correlation.
- [ ] Timeout, cancellation, retryability, stdout/stderr tail, and successful validation semantics remain unchanged.
- [ ] One table-driven sentinel regression covers the invocation-failure variants in the existing DTO channel.
- [ ] Classify published warning/summary text changes under the compatibility decision.

## Evidence

- Multiple successful validation DTO/warning branches embed raw `ex.Message` for dotnet-test and git invocation failures.
- This path is code-traced; add raw consumer-boundary coverage during implementation.
