---
category: Fixed
---

- **Fixed:** `replace_string_literals_preview` no longer throws `InvalidOperationException` when the requested literals appear nowhere in scope. `StringLiteralReplaceService.PreviewReplaceAsync` now returns a structured empty `RefactoringPreviewDto` (empty token, empty `Changes`, descriptive `Description`) matching `FixAllService`'s shape; input-validation failures (empty replacements array, empty `LiteralValue`, empty `ReplacementExpression`) still throw `ArgumentException` (`replace-string-literals-preview-throws-on-zero-match`).
