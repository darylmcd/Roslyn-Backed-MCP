---
category: Fixed
---

- **Fixed:** `diagnostic_details` now short-circuits to a single-document diagnostic scan when `filePath` + line/column are supplied, cutting typical latency from ~16s to ~2s on single-symbol probes (`DiagnosticService`). (`diagnostic-details-perf-budget-overrun`)
