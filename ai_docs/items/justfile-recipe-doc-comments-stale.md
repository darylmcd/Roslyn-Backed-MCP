# justfile-recipe-doc-comments-stale — Fix justfile recipe doc comments

**row:** `justfile-recipe-doc-comments-stale` · **pri:** `Low` · **size:** `M`

# justfile-recipe-doc-comments-stale — Fix justfile recipe doc comments

## Anchors

- `justfile`
- `eng/verify-version-drift.ps1`

## Acceptance

- [ ] `just --list` shows a full one-line description for every recipe.
- [ ] verify-version-drift comment matches eng/verify-version-drift.ps1 (seven files).

## Evidence

- `just --list` output and eng/verify-version-drift.ps1:4,124. (doc-audit 2026-09-23)
