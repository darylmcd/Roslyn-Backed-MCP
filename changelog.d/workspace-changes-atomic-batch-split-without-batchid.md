---
category: Fixed
---

- **Fixed:** `workspace_changes` splitting an atomic `apply_multi_file_edit` two-file batch into separate ledger entries: the multi-file apply path now records one consolidated change-tracker entry covering all affected files (matching the `apply_composite_preview` behavior), so `revert_apply_by_sequence` and callers reading `workspace_changes` see the batch as a single atomic unit. Fixes gh #740.
