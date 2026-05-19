---
category: Fixed
---

- **Fixed:** `find_duplicated_methods` no longer clusters xUnit `[Theory]` test methods (now excluded by syntactic attribute-name check) or symmetric `To*`/`From*` round-trip mapper pairs as copy-paste duplicates. Mapper pairs are still emitted but tagged with `clusterKind: "round-trip-mapper"` and a downranked similarity score so threshold-based callers can skip them. Fixes gh #768 §13.11.
