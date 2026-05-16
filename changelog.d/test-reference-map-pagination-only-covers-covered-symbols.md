---
category: Fixed
---

- **Fixed:** `test_reference_map` ignoring `limit` for mock-drift warnings: `mockDriftWarnings` is now bounded by a new `maxMockDriftWarnings` parameter (default 50) with `totalMockDriftCount`/`hasMoreMockDrift` metadata (fixes gh #774).
