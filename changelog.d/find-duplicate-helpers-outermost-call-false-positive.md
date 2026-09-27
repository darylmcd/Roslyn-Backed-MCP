---
category: Fixed
---

- **Fixed:** `find_duplicate_helpers` no longer reports helpers whose outermost call merely ends a pipeline (e.g. `…Select(…).ToArray()`); it now requires the call to forward exactly the helper's own parameters (receiver and arguments), so constant-argument specializations such as `s => s.Split(',')` are also no longer reported.
