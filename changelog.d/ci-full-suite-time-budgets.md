---
category: Maintenance
---

- **Maintenance:** Raise the single-job full-suite budgets to 90 minutes: the publish workflow's `publish` job (30) and the dispatch/schedule `linux-full` leg (45). The suite outgrew both: on `main` the three Linux PR shards total 44-53 minutes, the `linux-full` leg was cancelled at its cap while still making progress (not a hang), and a release publish would have timed out before packing.
