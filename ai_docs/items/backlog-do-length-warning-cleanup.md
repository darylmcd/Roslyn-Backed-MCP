# backlog-do-length-warning-cleanup — Clear backlog do-length warnings

**row:** `backlog-do-length-warning-cleanup` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/backlog.md` rows `audit-21-analyzer-load-decision` and `filewatcher-markstaleifrelevant-stale-precedence-comment`.

## Acceptance

- [ ] Use the sanctioned transactional writer for both index changes.
- [ ] Keep each row's decision/implementation trigger and source/type tags while reducing the `do` cell to the documented limit.
- [ ] Global backlog lint reports no `do-length` warnings.

## Evidence

- Intake validation reports exactly two pre-existing `do-length` warnings at 282 and 265 characters excluding tags.
