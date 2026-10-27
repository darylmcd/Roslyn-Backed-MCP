---
category: Fixed
---

- **Fixed:** Analyzer shadow cleanup preserves initializing process and workspace parents, requires released ownership or proven creator exit before pruning empty parents, and continues reclaiming stale workspace leases. Closes `analyzer-shadow-owner-startup-sweep-race`.
