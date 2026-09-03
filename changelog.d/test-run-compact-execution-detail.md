---
category: Added
---

- **Added:** `test_run` gained an opt-in `compact` parameter — when `true`, trims `execution.stdOut`/`stdErr`/`command`/`arguments`/`workingDirectory` (redundant with the count fields once a run demonstrably passed) and the failure-pagination fields when there is nothing to paginate. Default `false` preserves the existing response shape (#1421).
