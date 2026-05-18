---
category: Fixed
---

- **Fixed:** `get_coupling_metrics` — add `summary=true` mode returning per-project rollup counts (typeCount, avgInstability, classification buckets) without per-type detail rows. Resolves MCP token-cap overflow on 10+ project solutions (gh #763).
