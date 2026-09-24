---
category: Fixed
---
- **Fixed:** `replace_invocation_preview` no longer rewrites call sites inside the replacement method's own body (which produced infinite recursion when the replacement delegates to the old method).
