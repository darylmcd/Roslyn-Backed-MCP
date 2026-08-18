---
category: Fixed
---

- **Fixed:** `parameter_object_preview` no longer silently changes program behavior when a grouped parameter is a mutable value type. Mutating member calls and nested field/property writes through a struct parameter are now detected and refused before a preview token is created, naming the affected parameter and source location. Reference-type member mutation and array-element writes remain supported.
