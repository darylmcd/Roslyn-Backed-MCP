---
category: Fixed
---

- **Fixed:** `split_service_with_di_preview` no longer deletes other types, the base list, or constants declared in the same file as the split service. The facade now replaces only the source type's declaration inside the original file, keeps its attributes, constraints and non-method members, preserves the file's line endings, and injects retained fields its constructor assigned. Constructor and field shapes the generated facade cannot reproduce are now refused with a specific reason instead of silently changing behavior: primary or chained constructors, constructor logic beyond parameter-to-field copies, constructor assignments that override a field initializer, mutable fields that would exist on both the facade and a partition, and facade parameter-name collisions.
