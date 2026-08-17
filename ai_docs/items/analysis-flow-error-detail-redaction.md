# analysis-flow-error-detail-redaction — Sanitize analysis failure DTOs

**row:** `analysis-flow-error-detail-redaction` · **pri:** `High` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`
- `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs`
- `tests/RoslynMcp.Tests/FlowAnalysisTests.cs`

## Acceptance

- [ ] Data-flow and control-flow failure DTOs retain stable operation, range, category, remediation, and correlation without raw exception message, type, path, or secret-bearing value.
- [ ] Both services use the shared unexpected-exception reporter; cancellation still propagates.
- [ ] One table-driven nested-sentinel regression covers extract-method data flow plus direct data/control flow while healthy results remain unchanged.
- [ ] Classify the published text correction under the compatibility decision and changelog policy.

## Evidence

- `ExtractMethodService` and both `FlowAnalysisService` branches interpolate `ex.Message` into successful public failure DTOs.
- Found during the 2026-08-17 adjacent security review of the shared exception boundary.
