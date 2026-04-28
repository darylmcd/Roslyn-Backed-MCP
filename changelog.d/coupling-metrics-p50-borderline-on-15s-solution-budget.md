---
category: Changed
---

- **Changed:** `get_coupling_metrics` per-type symbol walk now runs in parallel via `Parallel.ForEachAsync` (DOP = `Environment.ProcessorCount`, capped by candidate count) and accumulates results into a `ConcurrentBag`. Profiling confirmed `SymbolFinder.FindReferencesAsync` awaited serially per type was the dominant cost — not workspace warm-up. SampleLib per-project budget test went 1000ms → 13ms; baseline against the 7-project / 571-doc reference solution will be re-measured against the released binary post-merge. Closes `coupling-metrics-p50-borderline-on-15s-solution-budget` (PR #476).
