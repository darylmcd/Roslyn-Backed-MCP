---
category: Fixed
---

- **Fixed:** `extract_type_preview` no longer refuses a member as having a dangling dependency on symbols declared inside the member itself (its own method type parameters, local functions, lambdas, query range variables); class-level type parameters and retained members are still refused.
