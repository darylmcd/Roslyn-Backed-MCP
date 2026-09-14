# resource-cache-hint-wire-failure-diagnostics — Diagnose missing resource cache hints

**row:** `resource-cache-hint-wire-failure-diagnostics` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/ResourceReadWireContractTests.cs` — ResourceRead_MigratedEndpointSuccess_KeepsCacheHintNormalizationPerEra
- `src/RoslynMcp.Host.Stdio/Middleware/ResourceReadResultFilter.cs` — Create and Normalize
- `src/RoslynMcp.Host.Stdio/ProtocolCompatibility/RequestProtocolFeatureGate.cs` — SupportsJuly2026Features

## Acceptance

- [ ] Assert cache-hint presence with the protocol era and captured synthetic response frame before reading values, so missing fields produce actionable evidence.
- [ ] Exercise repeated modern and legacy resource reads and identify whether the observed missing field is protocol state, response normalization, or test harness behavior; correct the evidenced cause within the anchored slice.
- [ ] Preserve modern ttlMs=0/private and legacy omission assertions; do not skip the test or classify a failed gate as success.

## Evidence

- PR #1478 CI run 34874822401, windows-hosted-1-of-4, failed at ResourceReadWireContractTests.cs:270 with KeyNotFoundException from GetProperty("ttlMs"). The captured response was not included in the failure, preventing causal diagnosis.
- This is an observed failure plus a confirmed assertion-diagnostic gap, not proof of a dependency regression or a confirmed flaky-test classification.
