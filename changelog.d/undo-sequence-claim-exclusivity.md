---
category: Fixed
---

- **Fixed:** `revert_apply_by_sequence` now resolves the target, checks later overlapping applies and claims the revert in one critical section, so two concurrent undos of the same sequence can no longer both restore its files; the loser is refused with `unknown-sequence` and another operation's pending snapshot is never dropped.
