---
category: Fixed
---

- **Fixed:** `change_signature_preview` `op=add` (and `remove`/`reorder`) now updates every implementation and call site when several live in the same document. Previously later declarations and callers in an already-edited document were skipped, leaving CS0535 and undercounting `callsiteUpdates`.
