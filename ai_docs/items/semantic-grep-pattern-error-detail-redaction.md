# semantic-grep-pattern-error-detail-redaction — Sanitize invalid-regex errors

**row:** `semantic-grep-pattern-error-detail-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,tool-error-envelope-sensitive-detail-disclosure`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SemanticGrepService.cs`
- `tests/RoslynMcp.Tests/SemanticGrepIntegrationTests.cs`

## Acceptance

- [ ] Invalid regex failures retain parameter identity and stable correction guidance without raw parser exception text, pattern content, paths, or secret-bearing values.
- [ ] Keep timeout, pagination, language, and successful match semantics unchanged.
- [ ] One table-driven sentinel regression covers malformed syntax and timeout-like patterns at the public tool envelope.
- [ ] Classify the published text correction under the compatibility decision and changelog policy.

## Evidence

- `SemanticGrepService` wraps `ArgumentException` with `Invalid regex pattern: {ex.Message}`, and the public expected-error boundary historically echoed that raw text.
- Found during the 2026-08-17 adjacent security review of expected tool errors.
