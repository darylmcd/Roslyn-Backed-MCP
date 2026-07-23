---
category: Fixed
---

- **Fixed:** `find_references_bulk` now enforces its documented 50-symbol batch cap (`ArgumentException` on overflow instead of silently accepting unbounded arrays); `get_complexity_metrics` now validates its `limit` parameter via `ParameterValidation.ValidatePagination`; `find_consumers` gained `limit`/`offset` parameters (default `limit=100`) with `hasMore`/`totals` reporting, matching the pagination contract already used by `find_type_usages`, `callers_callees`, and `symbol_relationships`.
