---
category: Fixed
---

- **Fixed:** `find_duplicated_methods` (and its `find_duplicated_code` alias) could exceed the MCP output cap on large solutions. Added an opt-in `summary` parameter (default `false`) that omits the per-member `Methods` array (file paths + line spans) and returns only group-level metadata (`normalizedHash`, `memberCount`, `similarity`, `lineCount`, `clusterKind`), reducing payload size 10–50× on solutions with many duplicate clusters.
