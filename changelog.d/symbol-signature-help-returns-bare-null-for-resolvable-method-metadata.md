---
category: Fixed
---

- **Fixed:** `symbol_signature_help` returning bare null for method metadataName inputs containing parenthesized parameter types (e.g. `Namespace.Type.Method(ParamType, CancellationToken)`). Adds the same qualified-signature fallback resolver that `callers_callees` received in gh #616. Fixes gh #747.
