---
category: Fixed
---

- **Fixed:** Extracted interfaces keep parameter semantics on every route: a contract attribute beside a default the interface rewrote no longer throws; a public `__arglist` method is extracted as `__arglist` instead of crashing every extraction of its type with an internal error; a constant default or attribute argument that names an instance field (`nameof(_field)`), a non-public member or, when the interface lands in another project, a member of the source assembly is written as its value (cross-project `OwnMax` becomes `4`, a source-assembly enum value becomes `(Hue)2`, while the standalone route keeps the symbolic form); a default or contract-attribute argument naming a constant of an otherwise unreferenced type imports that type's namespace; and negative enum values are parenthesized. Covers `cross-project-interface-parameter-semantics`.
