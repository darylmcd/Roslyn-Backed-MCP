# input-examples-feasibility-size-correction — Correct the input-examples feasibility size

**row:** `input-examples-feasibility-size-correction` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/backlog.md` row `input-examples-feasibility`.
- `ai_docs/items/input-examples-feasibility.md`

## Acceptance

- [ ] Confirm the existing pilot still intentionally spans three production files/tools.
- [ ] Use the sanctioned transactional writer to change its declared size from `S` to `M`; if the inventory is stale, reduce the concrete anchors instead.
- [ ] Keep the feasibility question and close-wont-do option unchanged.
- [ ] Global backlog lint reports no `size-vs-anchors` warning for the row.

## Evidence

- Intake validation reports three production anchors while the live row declares size `S`.
