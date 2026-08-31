---
category: Fixed
---

- **Fixed:** Concurrent diagnostic queries now enforce the eight-entry per-workspace result-cache cap atomically while preserving exact-filter reuse and newer workspace versions. Closes `diagnostic-query-result-cache-concurrent-cap`.
