---
category: Fixed
---
- **Fixed:** `test_coverage` and `get_test_coverage_map` now report unexpected failures as `isError: true` through the shared filter instead of a success-shaped `failureEnvelope` result; gate timeout keeps its structured Timeout result.
