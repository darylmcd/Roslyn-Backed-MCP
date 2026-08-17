# bulk-reference-error-detail-redaction — Sanitize bulk-reference item errors

**row:** `bulk-reference-error-detail-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`
- `tests/RoslynMcp.Tests/FindReferencesSummaryTests.cs`

## Acceptance

- [ ] Failed bulk-reference items preserve opaque-handle/metadata-name keys; file-locator keys become an explicit workspace-relative locator or stable item ordinal plus line/column, never an absolute local path.
- [ ] Preserve stable category/remediation without exception-derived message, additional path, or secret-bearing value.
- [ ] Server diagnostics consume the shared secret-safe projection and request correlation rather than duplicating exception formatting.
- [ ] One secret-sentinel per-item regression proves the published DTO is sanitized while successful bulk-reference results remain unchanged.
- [ ] Classify the published DTO text correction and add migration guidance if required.

## Evidence

- `ReferenceService` returns `ex.Message` in each failed `BulkReferenceResultDto`.
- This exposure is code-traced; implementation must exercise the consumer boundary rather than treating the trace as wire proof.
