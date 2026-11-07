---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` no longer drags enum, delegate and nested-namespace siblings into the destination namespace when a type's namespace is rewritten in place without a destination file (block and file-scoped); only the selected type moves and the siblings stay in the source namespace. Closes `relocation-same-file-rewrite-preserves-other-members`.
