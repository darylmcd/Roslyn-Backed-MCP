---
category: Maintenance
---

- **Maintenance:** Widen `pr-reconciler` subagent's pending-checks poll budget from 1×60s retry (~120s ceiling) to 12×60s retries (~12-min ceiling) with progress notifications between attempts. Failing checks still short-circuit the early-exit path. Removes false-`not-ready` returns on slow `validate` jobs. Closes `pr-reconciler-poll-budget-tightness`.
