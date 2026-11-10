---
category: Fixed
---

- **Fixed:** `test_related_files` and `test_related` no longer swallow semantic reference-sweep failures silently: the exception is logged, and `diagnostics.missReasons` gains one payload-safe "sweep did not complete" entry (count only, no paths or exception text) while `diagnostics.heuristicsAttempted` omits the heuristic whose sweep failed (`direct-reference`, `inbound-reference`, `namespace-neighbor`, `reference-sweep`); fallback matches are still returned and cancellation still propagates (`test-discovery-direct-reference-failure-observability`).
