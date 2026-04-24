---
category: Fixed
---

- **Fixed:** `find_base_members` and `find_overrides` now resolve metadata-boundary interfaces (e.g. `IEquatable<T>.Equals`) consistently with `member_hierarchy`; results expose `SymbolDto[]` (from `LocationDto[]`) so metadata members surface with `FilePath=null` instead of silently returning empty (`ReferenceService`). **BREAKING:** `IReferenceService.FindBaseMembersAsync` / `FindOverridesAsync` + `SymbolRelationshipsDto.BaseMembers` / `Overrides` now return `SymbolDto[]`. (`find-base-members-vs-member-hierarchy-metadata-drift`)
