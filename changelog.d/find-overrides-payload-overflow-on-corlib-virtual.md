---
category: Fixed
---

- **Fixed:** `find_overrides` returning an unbounded payload when anchored on corlib virtual members (`System.Object.ToString`, `Equals`, `GetHashCode`). The tool now returns `count=0` with an explanatory hint for corlib virtuals, matching the existing `symbol_relationships` suppression behavior. `member_hierarchy.overrides` benefits from the same guard via its shared `FindOverridesAsync` code path. Fixes gh #754.
