---
category: Fixed
---

- **Fixed:** `apply_text_edit`, `apply_project_mutation`, and `set_editorconfig_option` now preserve a source file's original BOM/encoding on write instead of silently re-encoding it as UTF-8-no-BOM; `AtomicFileWriter.WriteAllTextAsync` gained an optional `Encoding` parameter threaded from the pre-mutation `SourceText`/on-disk bytes at each call site (`apply_composite_preview`'s write path has the same underlying gap and is tracked separately).
