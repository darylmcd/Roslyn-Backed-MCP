---
category: Fixed
---

- **Fixed:** `validate_workspace`'s merged `errorDiagnostics`/`errorCount` no longer include an uncorroborated `Category=="Compiler"` row surfaced only by the `project_diagnostics` harvest when `compile_check` itself reports zero errors — a green build (clean `compile_check`, all related tests passing) now reports `overallStatus: "clean"` / `errorCount: 0` instead of the phantom `"analyzer-error"` / `errorCount: 1` that PR #1140 only relabeled from the pre-fix `"compile-error"`.
