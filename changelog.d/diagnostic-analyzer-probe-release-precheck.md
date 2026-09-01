---
category: Fixed
---

- **Fixed:** Error-only diagnostic queries now recover conservatively from hostile analyzer descriptor probes with correlated secret-safe operator reporting, while release verification now refuses low-memory or leaked-process environments before the expensive gate. Closes `diagnostic-analyzer-descriptor-enumeration-failure-isolation` and `release-cut-step3-environment-precheck`.
