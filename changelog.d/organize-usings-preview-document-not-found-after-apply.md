---
category: Fixed
---

- **Fixed:** `organize_usings_preview` returning `"Invalid operation: Document not found"` after an auto-reload cascade (e.g. `remove_dead_code_apply` earlier in the turn) while `format_document_preview` succeeded on the same file. Extracts `RoslynMcp.Roslyn.Helpers.DocumentResolution` — `GetDocumentFromFreshSolutionOrThrow` re-acquires the current `Solution` from `IWorkspaceManager` at call time so the post-auto-reload snapshot is honored, and `GetDocumentInSolutionOrThrow` handles accumulator paths. `RefactoringService` (organize-usings / format-document / format-range / code-fix previews) and `EditService.ResolveDocumentAndTextAsync` route through the shared helper with a unified `InvalidOperationException("Document not found: ...")` (`organize-usings-preview-document-not-found-after-apply`).
