---
category: Fixed
---

- **Fixed:** `split_service_with_di_preview` no longer deletes other types, the base list, or constants declared in the same file as the split service. The facade now replaces only the source type's declaration inside the original file, keeps its attributes, constraints and non-method members, and preserves the file's line endings.
