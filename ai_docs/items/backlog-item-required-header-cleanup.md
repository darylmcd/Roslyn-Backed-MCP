# backlog-item-required-header-cleanup — Restore required metadata headers on live backlog items

**row:** `backlog-item-required-header-cleanup` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/items/`

## Acceptance

- [ ] Add the canonical heading and row metadata cache to each live item currently reported by `row-detail-header-missing`; preserve the index as source of truth.
- [ ] Keep legacy design prose below the required item header without changing its implementation scope.
- [ ] Global backlog lint reports zero `row-detail-header-missing` warnings.

## Evidence

The 2026-09-01 cycle-4 backlog lint reported 19 `row-detail-header-missing` warnings across live rows. The existing `backlog-item-dependency-header-sync` row covers only dependency-cell drift and does not cover absent required headings or row metadata caches.
