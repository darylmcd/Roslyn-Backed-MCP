---
category: Fixed
---

- **Fixed:** Audit records that own no undo capture no longer re-promote an earlier operation's (or a failed or cancelled apply's) pending undo snapshot, so `revert_apply_by_sequence` on a no-effect audit cannot erase an earlier real apply. Each recorded change now carries the capture id of its own operation, and only that id promotes the snapshot into revert history.
