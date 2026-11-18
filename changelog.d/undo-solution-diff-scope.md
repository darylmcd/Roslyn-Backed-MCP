---
category: Fixed
---

- **Fixed:** A solution-snapshot revert (`revert_last_apply`, `revert_apply_by_sequence`) now rewrites only the documents the reverted apply owns (`undo-solution-diff-reverts-sibling-document-edits`, `undo-disk-walk-fallback-restores-drifted-sibling-docs`). A committed snapshot owns exactly its recorded affected files, so a later file-disjoint apply's edits (in the workspace and on disk) survive reverting an earlier apply, and the disk-walk fallback no longer rewrites documents that merely drifted on disk; an uncommitted snapshot owns only its own solution diff. Public behavior change: a committed solution-snapshot revert that restores no document of its own now returns `reverted=false` (by sequence: `reason=revert-failed`) with the snapshot retained, instead of `reverted=true`; an uncommitted solution-only revert whose workspace shows no change still returns `reverted=true` but no longer rewrites disk-only drift.
