---
category: Fixed
---

- **Fixed:** `code_fix_preview` returning an unhandled `InvalidOperationException` when no code fix provider is registered for the requested diagnostic ID. The tool now returns a structured envelope with empty `previewToken` and a `guidanceMessage`, consistent with `fix_all_preview`. Closes `code-fix-preview-vs-fix-all-preview-shape-inconsistency`. Fixes gh #768 §13.9.
