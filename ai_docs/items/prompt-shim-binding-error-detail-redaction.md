# prompt-shim-binding-error-detail-redaction — Sanitize prompt-shim binding failures

**row:** `prompt-shim-binding-error-detail-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,prompt-call-error-filter-boundary`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs`
- `tests/RoslynMcp.Tests/PromptShimToolsTests.cs`

## Acceptance

- [ ] Invalid JSON and per-property binding errors preserve the offending property name, expected schema/type, and correction example without echoing parser exception text, supplied JSON, full paths, or secret-bearing values.
- [ ] Unexpected binding failures use the shared secret-safe projection and correlation; expected format errors remain stable `InvalidArgument` results.
- [ ] One table-driven secret-sentinel regression covers document-level JSON plus per-property conversion and proves healthy prompt dispatch is unchanged.
- [ ] Classify the published text correction under the compatibility decision and changelog policy.

## Evidence

- `PromptShimTools` interpolates `JsonException.Message` into public invalid-JSON and property-conversion errors.
- Found during the 2026-08-17 adjacent security review of expected tool errors.
