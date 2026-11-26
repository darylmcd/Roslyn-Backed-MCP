# surface-test-latency-rubric — Redesign the surface-test latency scoring heuristic

**row:** `surface-test-latency-rubric` · **pri:** `Medium` · **size:** `S` · **verified:** `2689302e6f8d25674b5dd80f06d7b483a051e15e`

## Anchors

- `skills/mcp-server-surface-test/prompts/full.md:60`
- `skills/mcp-server-surface-test/prompts/phases/output-and-close.md:265`

## Acceptance

- [ ] Define a latency-scoring method that accounts for representative corpus size and operation class or uses measured matched baselines. Define the statistic, environment and fixture provenance.
- [ ] Keep the promotion heuristic distinct from cold per-case product acceptance and the request timeout. Update both prompt paths and demonstrate consistent scores on representative small and full-solution cases.

## Evidence

- L0, pre-existing, P2 (I5). The flat writer p50 heuristic of 30 seconds was applied as a strict cold full-solution limit without establishing that equivalence. Trigger: promotion guidance is reused as a product threshold. Harm: the resulting regression goal has no corpus or machine calibration.
- Operator explicitly requested a separate redesign item on 2026-11-26 (collab seq 164). The paired benchmark decision applies to Change Signature and does not change the shipped rubric.
