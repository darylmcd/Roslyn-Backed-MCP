---
category: Fixed
---

- **Fixed:** `apply_multi_file_edit` now reports `success: false` when any file fails syntax validation or application, and `filesModified` counts only completed successful file applications. Ordered per-file summaries, continued application of later files, `skipSyntaxCheck`, verification, and whole-batch recovery through `revert_last_apply` retain their existing behavior.
