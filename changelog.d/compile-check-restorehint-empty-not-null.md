---
category: Fixed
---

- **Fixed:** `compile_check`'s `restoreHint` field now serializes as `null` (not an empty string) when no restore/zero-projects/file-filter-fallback hint applies, matching `BuildHint`'s documented contract. Callers that probed for hint presence via null-checks were previously always seeing a truthy empty string on every clean response. Closes `compile-check-restorehint-empty-not-null`.
