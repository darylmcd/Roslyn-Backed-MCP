---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` now refuses an enum or delegate selection with `type-kind Enum not supported` / `type-kind Delegate not supported` (naming the tool and the supported kinds) instead of the misleading "was not found in the workspace"; a genuine miss still reads "not found". The refusal text is built by one helper shared with `move_type_to_file_preview`. Closes `relocation-selected-enum-delegate-reported-not-found`.
