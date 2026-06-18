---
category: Fixed
---

- **Fixed:** `symbol_signature_help` now returns a structured `{error, category: NotFound, message}` envelope for unresolvable locators instead of a bare JSON `null`, matching its sibling tools.
