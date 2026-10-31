---
category: Fixed
---

- **Fixed:** Metadata member candidate handles now preserve exact assembly and declaration identity, so an overload selected from `find_references` can be passed back for references without losing its signature. Covers `metadata-member-symbol-handle-roundtrip` and `external-overload-candidate-handle-roundtrip`.
