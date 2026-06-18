---
category: Fixed
---

- **Fixed:** `IsCorlibAssembly` heuristic in `find_implementations` now uses a `SpecialType`-based primary gate plus a name signal (BCL allowlist or `System.*`) corroborated by a well-known .NET/BCL strong-name public-key token, instead of a bare `StartsWith("System.")` match — a third-party `System.MyCompany.Foo` assembly (no `SpecialType`, not BCL-signed) is no longer misclassified as a corlib implementation root.
