---
category: Fixed
---

- **Fixed:** `IsCorlibAssembly` heuristic in `find_implementations` now uses a `SpecialType`-based primary gate plus an explicit BCL allowlist instead of a broad `StartsWith("System.")` match, preventing third-party `System.*`-named assemblies from being misclassified as corlib implementation roots.
