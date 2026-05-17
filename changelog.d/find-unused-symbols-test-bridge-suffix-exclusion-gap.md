---
category: Fixed
---

- **Fixed:** `find_unused_symbols` false positives for test-bridge accessor methods: names ending in `ForTest`, `ForTesting`, `_ForTest`, or `Internal` are now excluded when `excludeConventionInvoked=true` (default), matching the standard test-bridge accessor pattern (fixes gh #775).
