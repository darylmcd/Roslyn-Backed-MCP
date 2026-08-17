# workspace-readiness-probe-error-redaction — Sanitize readiness probe limitations

**row:** `workspace-readiness-probe-error-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` — source-generated-document readiness probe.
- `tests/RoslynMcp.Tests/WorkspaceReadinessReportTests.cs`

## Acceptance

- [ ] Replace exception-type-derived limitation text with a stable category and actionable retry guidance.
- [ ] Keep raw type, message, path, and secret-bearing values out of both response channels.
- [ ] Preserve `OperationCanceledException` propagation and retain secret-safe correlated structure in an enabled capture sink.
- [ ] One sentinel probe-failure regression proves the readiness report remains useful without disclosure.

## Evidence

- The readiness report catches every non-cancellation exception and publishes `ex.GetType().Name` inside a successful public DTO.
