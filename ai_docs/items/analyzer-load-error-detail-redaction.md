# analyzer-load-error-detail-redaction — Sanitize analyzer-load domain errors

**row:** `analyzer-load-error-detail-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Roslyn/Services/AnalyzerInfoService.cs`
- `tests/RoslynMcp.Tests/AnalyzerInfoToolsTests.cs`

## Acceptance

- [ ] Analyzer-load failures preserve stable rule/category/assembly identity without raw exception message, local path, or secret-bearing value.
- [ ] Server diagnostics consume the shared secret-safe projection and request correlation rather than duplicating exception formatting.
- [ ] One secret-sentinel analyzer-load regression proves the published DTO is sanitized while successful analyzer results remain unchanged.
- [ ] Classify the published DTO text correction and add migration guidance if required.

## Evidence

- `AnalyzerInfoService` returns `Failed to load: {ex.Message}` in a successful rule DTO.
- This exposure is code-traced; implementation must exercise the consumer boundary rather than treating the trace as wire proof.
