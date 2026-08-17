# backlog-item-dependency-header-sync — Synchronize backlog item dependency headers

**row:** `backlog-item-dependency-header-sync` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/items/dedupe-namespace-folder-segment-resolution.md`
- `ai_docs/items/apply-undo-tool-response-contract-docs.md`
- `ai_docs/items/refactoring-code-fix-preview-decomposition.md`

## Acceptance

- [ ] Add the exact live index dependency cell to each item's metadata header.
- [ ] Do not alter dependency direction, row priority, size, anchors, or acceptance scope.
- [ ] Global backlog lint reports no `index-header-desync` warnings for the three items.

## Evidence

- Intake validation originally found four item headers with absent dependencies; updating the touched prompt-smoke row synchronized one, leaving the three anchored warnings.
