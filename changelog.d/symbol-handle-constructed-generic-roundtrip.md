---
category: Fixed
---

- **Fixed:** Newly generated handles for supported constructed named types preserve type arguments, assembly identity, outer type substitutions, tuple labels, and nullability. Existing definition and member handles keep their bytes and behavior. Request a fresh handle to recover the construction from a previously emitted lossy handle.
