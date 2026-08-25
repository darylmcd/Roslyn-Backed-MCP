---
category: Maintenance
---

- **Maintenance:** Standardized parameter descriptions across the code-action, suppression, symbol-refactor, and bulk-refactoring tool surfaces to canonical one-liners, and added a reflection-driven contract test that locks the canonical `workspaceId` / `filePath` forms (with an explicit allow-list for load-bearing exceptions such as `set_diagnostic_severity.filePath`) so phrasing drift cannot re-accumulate.
