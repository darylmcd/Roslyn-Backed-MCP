# deep-review-program-shape-list-duplication — a seventh site the single-sourcing pass did not cover

## Anchors

- `ai_docs/procedures/deep-review-program.md`
- `eng/stage-review-inbox.ps1`

## Acceptance

- [ ] `deep-review-program.md` points at the canonical Recognized-shapes block in `eng/stage-review-inbox.ps1` instead of re-listing the globs.
- [ ] A repo-wide grep for the raw glob triple returns only the canonical block itself.

## Evidence

Traced during the PR #1236 review: that PR closed the last 2 of an originally-cited 6 duplication sites and its changelog fragment says so, but `deep-review-program.md` still inlines the same `*_mcp-server-audit.md` / `*_experimental-promotion.md` / `*_roslyn-mcp-retro.md` triple — a 7th site the original count missed and the plan explicitly deferred.

Consequence if left: a reader of the changelog infers single-sourcing is complete while one runbook still duplicates a 3-of-6 subset, which is how the original drift started.
