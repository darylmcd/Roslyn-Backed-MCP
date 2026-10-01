---
category: Fixed
---

- **Fixed:** `extract_type_preview` keeps extracted members at their original accessibility and widens to `public` only members the retained source type still references; previously every extracted private field and helper became `public`.
