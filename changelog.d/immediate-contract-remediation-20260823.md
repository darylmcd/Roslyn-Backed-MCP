---
category: Fixed
---

- **Fixed:** Prompt calls validate caller arguments at an owned pre-dispatch boundary, keep handler failures sanitized, and share one service-parameter classifier across catalog and shim paths.
- **Fixed:** Cohesion scans expose completeness and failed-type counts, propagate cancellation, and prevent legacy consumers from treating partial metrics as complete.
- **Fixed:** Shared test cleanup runs every disposal step, reports aggregate failures, disposes outside the initialization lock, and removes sync-over-async metadata tests.

Closes `get-prompt-binding-stage-contract-adapter`, `prompt-service-parameter-classifier-consolidation`, `cohesion-scan-completeness-contract`, `host-process-metadata-tests-async-await`, and `test-assembly-cleanup-failure-observability`.
