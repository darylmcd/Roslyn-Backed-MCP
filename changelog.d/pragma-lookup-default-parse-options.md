---
category: Fixed
---

- **Fixed:** `add_pragma_suppression`, `verify_pragma_suppresses`, and `pragma_scope_widen` now interpret symbol-guarded pragma directives using the selected document project's parse options and ignore inactive pragmas. Adding a suppression no longer silently skips an insertion because of an inactive disable, verification reports the active pair, and widening a file whose only matching pair is inactive now refuses with `success: false` and the existing no-pair reason fields without changing the file.
