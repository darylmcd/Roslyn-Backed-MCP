---
category: Maintenance
---

- **Maintenance:** Standardized `workspaceId`, `previewToken` and required `filePath` parameter descriptions to the canonical one-liner across the refactoring-core tool cluster (`rename`/`organize_usings`/`format_document`/`format_range`/`code_fix`/`format_check`, `restructure_preview`, `replace_string_literals_preview`, `get_operations`), and removed an internal tracker id that was leaking into the `rename_preview` wire schema. Load-bearing parameter guidance (rename summary mode, restructure placeholder contract, UX-003 column targeting, script timeout budget) is unchanged and is now locked by the shared canonicalization ratchet, which this slice extends instead of adding another per-slice guard.
