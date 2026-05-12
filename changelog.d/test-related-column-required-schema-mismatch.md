---
category: Fixed
---

- **Fixed:** `test_related` schema now documents the conditional-required-as-a-group relationship between `filePath`, `line`, and `column`. Callers using source-location mode must supply all three together; callers using `symbolHandle` or `metadataName` mode omit all three. Fixes gh #618.
