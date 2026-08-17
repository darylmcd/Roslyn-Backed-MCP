# test-runner-timeout-error-detail-redaction — Sanitize runner timeout envelopes

**row:** `test-runner-timeout-error-detail-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`
- `tests/RoslynMcp.Tests/TestRunnerTimeoutTests.cs`

## Acceptance

- [ ] Timeout DTOs retain timeout category, retryability, command shape, and recovery guidance without raw exception message, path, or secret-bearing value.
- [ ] Server diagnostics use the shared secret-safe projection and correlation; caller cancellation remains distinct and propagates.
- [ ] One nested-sentinel timeout regression proves both public and enabled-sink boundaries are sanitized while ordinary nonzero test results remain unchanged.
- [ ] Classify the published text correction under the compatibility decision and changelog policy.

## Evidence

- `TestRunnerService` copies `ex.Message` into both `CommandExecutionDto.StdErr` and `DotnetOutputParser.BuildTimeoutResult`.
- Found during the 2026-08-17 adjacent security review of workspace-validation recovery.
