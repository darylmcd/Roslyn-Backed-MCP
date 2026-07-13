---
category: Fixed
---

- **Fixed:** `ParameterValidation.ValidatePagination` now rejects `limit` values above a defined maximum (1000), preventing unbounded materialization when a caller supplies an excessively large page size.
