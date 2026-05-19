---
category: Fixed
---

- **Fixed:** `test_coverage` no longer fails the entire call when some test projects lack `coverlet.collector`. Projects without the collector are now skipped and listed in a new `coverageGaps` field; partial coverage is returned with `success=true`. Closes `test-coverage-fail-fast-on-missing-coverlet`. Fixes gh #768 §13.12.
