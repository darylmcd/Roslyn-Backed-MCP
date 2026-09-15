---
category: Fixed
---

- **Fixed:** Validation now terminates and drains its owned Git process on cancellation, timeout, or failure; test-results cleanup errors preserve primary outcomes and use safe diagnostics; formatter fixtures use the shared asynchronous process runner before deleting their files. Closes `workspace-validation-git-process-lifetime`, `test-runner-results-cleanup-failure-precedence`, and `changed-format-test-process-runner-lifetime`.
