---
category: Fixed
---

- **Fixed:** `extract_type_preview` now refuses, before storing a token, an extraction whose rewritten source project adds compiler errors (for example CS0708 on a static host type, CS0751 on a partial method, CS0534 on an abstract override), listing every added diagnostic id. Closes `extract-type-preview-compile-delta-gate`.
