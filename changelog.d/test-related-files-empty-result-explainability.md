---
category: Added
---

- **Added:** `test_related_files` and `test_related` empty-result responses now carry a `diagnostics` envelope exposing `scannedTestProjects`, `heuristicsAttempted[]`, and `missReasons[]` so callers can distinguish "no tests exist" from "the heuristic missed them." `TestDiscoveryService` populates per-file miss reasons (path did not resolve, no syntax tree, no name-term match) and `WorkspaceValidationService` now flags the empty-changed-files short-circuit explicitly. `RelatedTestsForSymbolDto` was extended in lockstep so the `test-related-response-envelope-parity` contract is preserved (`test-related-files-empty-result-explainability`).
