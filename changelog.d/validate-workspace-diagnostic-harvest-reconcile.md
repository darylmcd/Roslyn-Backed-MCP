---
category: Fixed
---

- **Fixed:** `validate_workspace`'s merged `errorDiagnostics`/`errorCount` no longer include an uncorroborated `Category=="Compiler"` row surfaced only by the `project_diagnostics` harvest when a **complete** `compile_check` pass reports zero errors — a green build (clean `compile_check`, all related tests passing) now reports `overallStatus: "clean"` / `errorCount: 0` instead of the phantom `"analyzer-error"` / `errorCount: 1` that PR #1140 only relabeled from the pre-fix `"compile-error"`. A `compile_check` pass that was cancelled, or that did not reach every project, forfeits that authority and keeps the second harvest's compiler rows — so a timed-out compile hiding real `CS*` errors still reports non-clean rather than a false green.
