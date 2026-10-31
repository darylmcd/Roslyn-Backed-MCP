---
category: Fixed
---

- **Fixed:** `extract_type` now refuses a preview when base construction can dispatch into a retained override or interface implementation that uses the extracted helper before constructor injection assigns it.
