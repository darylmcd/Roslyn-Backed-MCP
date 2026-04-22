---
category: Fixed
---

- **Fixed:** `preview_multi_file_edit` now rejects edits that leave invalid C# when `skipSyntaxCheck=false` (F17-style namespace / trivia cases where the parser previously recovered without surfacing errors). Tightens `EditService` syntax validation; adds `PreviewMultiFileEditSyntaxRegressionTests`; tool description aligned in `MultiFileEditTools`. Closes `preview-multi-file-edit-silent-syntax-acceptance` (PR #349).
