---
category: Fixed
---

- **Fixed:** Undo snapshots now retain the boundary-canonical write target so link swaps cannot redirect reverts, and root-expansion grants are revoked from the shared workspace lifecycle on explicit close, LRU eviction, and shutdown. Closes `undo-revert-uncanonicalized-restore-path` and `root-expansion-grant-revoke-on-lifecycle-event`.
