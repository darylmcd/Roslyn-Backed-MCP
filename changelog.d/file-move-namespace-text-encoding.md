---
category: Fixed
---

- **Fixed:** `move_file_preview` with `updateNamespace` now keeps the moved file's original encoding and byte-order mark (for example UTF-16 with BOM) through block- and file-scoped namespace rewrites. Closes `file-move-namespace-text-encoding`.
