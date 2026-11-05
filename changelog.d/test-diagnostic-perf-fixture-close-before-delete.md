---
category: Fixed
---

- **Fixed:** Diagnostic performance tests now close their private workspace before deleting its copied solution and remove the copy if initialization fails. Cleanup attempts remain independent, and regression tests verify session and directory ownership. Closes `test-diagnostic-perf-fixture-close-before-delete`.
