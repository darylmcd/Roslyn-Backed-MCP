# public-exception-detail-policy — Single-source public exception sanitization

**row:** `public-exception-detail-policy` · **pri:** `High` · **size:** `S`

## Anchors

- New focused policy under `src/RoslynMcp.Core/`.
- New `tests/RoslynMcp.Tests/PublicExceptionDetailPolicyTests.cs`.

## Acceptance

- [ ] Expose a layering-safe policy for unexpected public errors that Host and Roslyn services can consume without upward references.
- [ ] Produce stable client category/remediation/correlation data without raw message, type, inner text, stack, path, or secret-bearing value.
- [ ] Produce a separate server diagnostic projection retaining correlation and benign type/inner/stack structure; secret-bearing or raw user-derived message values are never present.
- [ ] One table-driven nested-exception/path/secret-sentinel test proves deterministic output and no sentinel at either boundary.
- [ ] Expected validation/domain text is not routed through this unexpected-error policy.

## Evidence

- Tool, prompt, sampling, coverage, analyzer, reference, scaffolding, and validation paths each need the same public/internal boundary.
- Implementing that policy independently in Host and Roslyn would create a layering violation or duplicated security logic.
