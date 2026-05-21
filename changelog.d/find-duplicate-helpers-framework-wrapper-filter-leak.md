---
category: Fixed
---

- **Fixed:** `find_duplicate_helpers` now filters common framework glue wrappers for Serilog hosting, CORS service registration, and HTTP resilience extension APIs while still reporting true local duplicate helpers. Closes `find-duplicate-helpers-framework-wrapper-filter-leak`.
