---
category: Fixed
---

- **Fixed:** Interface extraction now qualifies constants named in member-, accessor- and type-parameter-level contract attribute arguments (same-assembly receivers keep the symbol, other assemblies get the value), drops non-contract type-parameter attributes, references the owner of fully qualified constants in kept attributes and defaults, and keeps `allows ref struct` and the other inherited constraints on overrides. Closes `interface-projector-attribute-scope-and-inheritance`, `interface-projector-ref-return-followups`.
