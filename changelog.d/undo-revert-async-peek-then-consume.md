---
category: Fixed
---

- **Fixed:** `revert_last_apply` now claims the pending undo snapshot atomically (with its history entry) before restoring, so a concurrent `revert_apply_by_sequence` of the same operation is refused instead of restoring it twice. A failed or cancelled restore undoes the claim, and a by-sequence target that was never the pending snapshot is no longer re-installed as the last-apply slot after a failed restore.
