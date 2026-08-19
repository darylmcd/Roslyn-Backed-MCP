---
category: Fixed
---

- **Fixed:** `parameter_object_preview` now validates every grouped parameter type before emitting the generated record — it refuses (instead of producing an uncompilable preview) when a parameter type depends on a method or containing-type type parameter, is less accessible than the chosen record visibility, or is unavailable from the selected DTO project. Refusals name the offending parameter and type, and no preview token is stored.
