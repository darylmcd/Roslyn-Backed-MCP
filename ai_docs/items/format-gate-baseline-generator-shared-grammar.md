# format-gate-baseline-generator-shared-grammar — share the formatter diagnostic grammar

**row:** `format-gate-baseline-generator-shared-grammar` · **pri:** `Medium` · **size:** `M` · **deps:** `changed-format-gate-diagnostic-id-contract`

## Anchors

- `eng/verify-changed-format.ps1:76`
- `eng/verify-changed-format.ps1:208`
- `eng/generate-format-baseline.ps1:54`
- `eng/generate-format-baseline.ps1:108`

## Acceptance

- [ ] The diagnostic regex and the truncation marker exist in exactly one place, consumed by both scripts (a shared `eng/` module or dot-sourced fragment).
- [ ] A test fails if the two consumers ever diverge.
- [ ] The gate's comment claiming the two "can never disagree" is either made true by construction or removed.

## Evidence

Verified byte-identical duplication in the PR #1356 diff: `verify-changed-format.ps1:208` and `generate-format-baseline.ps1:108` carry the same regex literal; `:76` and `:54` carry the same truncation marker string, differing only in quote style. Nothing asserts the pair stays in sync.

## Context

Surfaced by cold code-quality review of `format-changed-file-gate` (PR #1356, sweep `20260825T151721Z`). Depends on `changed-format-gate-diagnostic-id-contract` because that row may change which ids the grammar must recognize; sequencing avoids two PRs editing the same two scripts.
