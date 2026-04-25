---
category: Fixed
---

- **Fixed:** `test_related_files` no longer returns empty on multi-file service refactors whose test class names lack textual affinity to the changed sources — when the primary name-affinity heuristic misses, `TestDiscoveryService` now broadens via two 1-hop expansions (inbound-reference walk + namespace-neighbor name match), running only on the empty-primary path so amortized cost stays low. The `diagnostics.heuristicsAttempted` list reports `inbound-reference` / `namespace-neighbor` when broadening fires, and `missReasons` records the recovery count (`test-related-files-service-refactor-underreporting`).
