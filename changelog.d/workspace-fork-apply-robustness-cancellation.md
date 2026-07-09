---
category: Fixed
---

- **Fixed:** `workspace_fork_apply`'s copy/cleanup are now cancellable, concurrent fork-apply against the same source root is serialized, and `test_coverage` no longer misreports genuine caller cancellation as a timeout.
