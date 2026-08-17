# fixall-provider-error-detail-redaction — Sanitize FixAll provider failures

**row:** `fixall-provider-error-detail-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllService.cs`
- `tests/RoslynMcp.Tests/FixAllServiceTests.cs`

## Acceptance

- [ ] Preserve `FixAllProviderCrash`, diagnostic ID, requested scope, zero-change state, per-occurrence fallback flag, and actionable `code_fix_preview`/scope remediation.
- [ ] Remove exception type, raw message, path, and secret-bearing values from `GuidanceMessage`; retain a stable public provider-failure summary and correlation reference.
- [ ] Send only the shared secret-safe diagnostic projection to server observability.
- [ ] Invert the tests that currently require `Sequence contains no elements` and `InvalidOperationException`; one nested secret-sentinel regression proves absence at both boundaries.
- [ ] Classify the published guidance correction and add migration guidance if required.

## Evidence

- `BuildProviderCrashEnvelope` interpolates `ex.GetType().Name` and `ex.Message` into the successful `FixAllPreviewDto` failure envelope.
- Existing tests explicitly require both the raw provider message and exception type in client guidance.
