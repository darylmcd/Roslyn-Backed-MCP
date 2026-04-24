---
category: Fixed
---

- **Fixed:** `workspace_changes` now logs `set_editorconfig_option` / `set_diagnostic_severity` applies and preserves discriminated originating tool names (`rename_apply`, `code_fix_apply`, `format_document_apply`, `remove_dead_code_apply`, …) instead of collapsing them under generic buckets (`ChangeTracker` + `EditorConfigTools`). **BREAKING:** `IRefactoringService` / `IEditService` / `IEditorConfigService` mutation methods now require a `toolName` parameter; all internal callers updated. (`workspace-changes-log-missing-editorconfig-writers`)
