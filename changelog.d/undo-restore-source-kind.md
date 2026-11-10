---
category: Fixed
---

- **Fixed:** Solution-snapshot undo (`revert_last_apply` / `revert_apply_by_sequence`) now restores each changed document's pre-apply `SourceCodeKind` together with its text, instead of leaving an applied kind change in the workspace solution.
