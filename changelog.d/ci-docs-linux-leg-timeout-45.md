---
category: Changed
---

- **Changed:** The four hosted `docs-linux-N-of-4` validation legs now have a 45-minute job cap (was 30), matching the `linux-N-of-4` code legs raised earlier. Documentation-only PRs run the same test shards, and `docs-linux-2` was cancelled at the old cap (30m18s) while `docs-linux-1` took 23m57s. Temporary: the caps return to 30 minutes once the ubuntu duration profile is refreshed and the shards are rebalanced.
