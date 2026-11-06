---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` keeps `using <old>;` (with the kept-using warning) in consumers when an enum or delegate remains in the old namespace in another file or project, or when a second `namespace <old>` block of the source file remains during an in-place change; the cross-document count recognized only class-like declarations (`relocation-consumer-using-counts-enum-delegate-members`).
