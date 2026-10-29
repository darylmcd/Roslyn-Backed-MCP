---
category: Fixed
---

- **Fixed:** Interface extraction (standalone and cross-project) now emits `global using` directives before ordinary usings, so generated interface files no longer fail with CS8915 when the source file carries global usings. Closes `generated-interface-global-using-order`.
