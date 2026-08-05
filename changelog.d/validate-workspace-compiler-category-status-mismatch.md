---
category: Fixed
---

- **Fixed:** `validate_workspace`'s `overallStatus` no longer reports `compile-error` on an otherwise-clean build when the second, independently-harvested diagnostics pass (`project_diagnostics`) surfaces a Category="Compiler" error that the authoritative `compile_check` pass (`compile.ErrorCount`) does not corroborate — such a diagnostic now surfaces as `analyzer-error` instead of the misleading `compile-error` verdict (previously it could also mask a genuinely clean build behind a phantom compiler failure). `compile.ErrorCount > 0` is now the sole signal gating `compile-error`.
