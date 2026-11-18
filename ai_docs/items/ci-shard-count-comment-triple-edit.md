# ci-shard-count-comment-triple-edit — Single-source the shard-count routing summary

**row:** `ci-shard-count-comment-triple-edit` · **pri:** `Low` · **size:** `M` · **verified:** `f903fb6c1b3f5b94d38b6a970e17796b58ed6c8e`


## Anchors

- `eng/resolve-ci-topology.ps1`
- `.github/workflows/ci.yml`

## Acceptance

- [ ] A shard-count change edits one place; contract tests green.

## Evidence

CI_POLICY split agent; CI_POLICY.md now states counts once in its topology table. (doc-audit 2026-11-17)
