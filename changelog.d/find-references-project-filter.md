---
category: Added
---

- **Added:** `find_references` and `find_consumers` accept an optional `projectFilter` parameter (single project name or comma-separated list) for scoping the reference walk to a project subset, matching `semantic_grep`'s existing surface. Filter applies after the reference walk; null/absent yields byte-identical results to the prior behavior. Closes `find-references-project-filter`.
