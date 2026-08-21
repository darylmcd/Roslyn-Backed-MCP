# backlog-size-anchor-lint-reconciliation — Reconcile global backlog anchor and metadata lint

**row:** `backlog-size-anchor-lint-reconciliation` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/backlog.md`
- `ai_docs/items/` — update only the live rows named by global lint through the sanctioned transactional writer.

## Acceptance

- [ ] Split or resize every live `size-vs-anchors` error without weakening its implementation scope.
- [ ] Synchronize dependency headers, acceptance anchors, and the remaining oversized `do` cell.
- [ ] The canonical global backlog audit reports zero errors and warnings in one regression run.

## Evidence

- The current global audit reports six size/anchor errors and eight metadata warnings that predate this remediation batch.
