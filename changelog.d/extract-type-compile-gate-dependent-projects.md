---
category: Fixed
---

- **Fixed:** `extract_type_preview` now compiles every project that depends on the source project and refuses a preview that would break one (for example a `new Host()` site after an instance move, or a derived class using a moved protected member), listing the introduced diagnostic ids with project and file:line, instead of issuing a token that breaks the dependent on apply.
