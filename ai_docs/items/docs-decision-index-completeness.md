# docs-decision-index-completeness — docs-decision-index-completeness

**row:** `docs-decision-index-completeness` · **pri:** `Low` · **size:** `S`

# docs-decision-index-completeness — Keep shipped decisions discoverable

## Anchors

- `docs/README.md`
- `eng/verify-ai-docs.ps1`

## Acceptance

- Every shipped `docs/decisions/*.md` decision is indexed exactly once by `docs/README.md`.
- The AI-documentation gate fails with a bounded actionable diagnostic when a synthetic decision is unindexed or indexed more than once.
- The gate ignores non-decision files and preserves the existing decision ordering contract.

## Regression

Exercise the verifier against an isolated fixture containing one indexed decision, then add an unindexed decision and a duplicate index entry; each invalid shape must fail for the intended reason.

## Evidence

During the 2026-09-04 Tasks compatibility decision, `docs/README.md` was found to omit the already-shipped ADR 0009. Manual index maintenance has no completeness ratchet.
