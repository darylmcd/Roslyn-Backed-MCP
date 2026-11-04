---
category: Fixed
---

- **Fixed:** Timed correctness tests now cancel and settle their underlying workspace operations before releasing shared fixtures, while preserving timeout and cleanup failures. Closes `timing-test-guard-operation-ownership`.
