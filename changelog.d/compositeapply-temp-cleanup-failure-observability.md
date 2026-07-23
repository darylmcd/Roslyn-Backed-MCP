---
category: Fixed
---

- **Fixed:** `CompositeApplyOrchestrator`'s temp-file cleanup failures (a stray `.tmp` sibling that fails to delete after a failed write) are now logged as a warning with the path and exception instead of being silently discarded; the primary apply failure/result is unaffected.
