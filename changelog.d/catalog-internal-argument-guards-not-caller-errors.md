---
category: Changed
---
- **Changed:** Catalog output-schema declaration, alias-registry and structured-result internal invariants now throw `InvalidOperationException` instead of `ArgumentException`/`ArgumentOutOfRangeException`, so misuse is no longer classified as a caller error.
