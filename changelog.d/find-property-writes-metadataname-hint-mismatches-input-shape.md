---
category: Fixed
---

- **Fixed:** `find_property_writes` returning position-shaped hint text (`"Position resolved to a Field"`, `"Verify the column points at the symbol identifier"`) when the caller supplied `metadataName` instead of `filePath+line+column`. The `hint` field in the response now reflects the locator mode that was actually used, matching the behavior of other locator-aware error messages in the server. Closes gh #758.
