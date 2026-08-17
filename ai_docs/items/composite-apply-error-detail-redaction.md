# composite-apply-error-detail-redaction — Sanitize partial composite-apply failures

**row:** `composite-apply-error-detail-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] Preserve `Success: false`, the exact already-applied file set, partial-apply count, token validity, and recovery semantics after a mid-loop IO/authorization/invalid-operation failure.
- [ ] Replace raw exception type/message and unrestricted failing-path prose with a stable category, sanitized workspace-relative target identity when actionable, and remediation/correlation data.
- [ ] Send only the shared secret-safe diagnostic projection to an enabled capture sink; neither public DTO nor captured diagnostics contains a nested secret sentinel.
- [ ] One partial-apply sentinel regression proves the exact intentional `AppliedFiles` contract remains reported while the failing target and exception prose expose no absolute path or raw detail; the zero-write branch uses the same public contract.
- [ ] Classify the published `ApplyResultDto.Message` correction and add migration guidance if required.

## Evidence

- The catch currently returns `ex.Message` directly for a zero-write failure and appends it after the full failing path for a partial write.
- The same catch logs the raw exception and failing path, so fixing only the DTO would leave the deprecated MCP logging bridge as a second disclosure path.
