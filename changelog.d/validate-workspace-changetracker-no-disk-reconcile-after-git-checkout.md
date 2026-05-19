---
category: Fixed
---

- **Fixed:** `validate_workspace(changedFilePaths=null)` returning stale file paths in `changedFilePaths` after an out-of-band revert (e.g. `git checkout -- <file>`). The ChangeTracker auto-scope list is now reconciled against `git status` before being used as the validation scope; reverted files are excluded. Fixes gh #738.
